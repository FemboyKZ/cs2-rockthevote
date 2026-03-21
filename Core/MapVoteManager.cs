using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;
using CS2MenuManager.API.Class;
using CS2MenuManager.API.Enum;
using CS2MenuManager.API.Menu;
using Microsoft.Extensions.Logging;

namespace cs2_rockthevote
{
    public class MapVoteManager : IPluginDependency<Plugin, Config>
    {
        private readonly ILogger<MapVoteManager> _logger;
        private readonly MapLister _mapLister;
        private readonly ChangeMapManager _changeMapManager;
        private readonly NominationCommand _nominationManager;
        private readonly StringLocalizer _localizer;
        private readonly PluginState _pluginState;
        private Timer? Timer;
        private Timer? _chatMapChoiceTimer;
        List<string> mapsElected = new();
        private readonly Dictionary<int, string> _playerVotes = new();
        private readonly List<string> _currentVoteOptions = new();
        private readonly Dictionary<string, ItemOption> _optionItems = new();
        private bool _activeVoteIsRtv = false;
        private bool _allVotedShortened = false;
        private Plugin? _plugin;

        public int TimeLeft { get; private set; } = -1;
        public Dictionary<string,int> Votes { get; private set; } = new();
        private bool _isRunoff = false;

        private GeneralConfig _generalConfig = new();
        private MapVoteConfig _mapVoteConfig = new();
        private RtvConfig _rtvConfig = new();

        public event Action? VoteEndedNoVotes;

        public MapVoteManager
        (
            MapLister mapLister,
            ChangeMapManager changeMapManager,
            NominationCommand nominationManager,
            StringLocalizer localizer,
            PluginState pluginState,
            ILogger<MapVoteManager> logger
        )
        {
            _mapLister = mapLister;
            _changeMapManager = changeMapManager;
            _nominationManager = nominationManager;
            _localizer = localizer;
            _pluginState = pluginState;
            _logger = logger;
        }

        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin;
            _plugin.AddCommand("revote", "Re-open the active map vote menu.", OnRevoteCommand);
        }

        public void OnConfigParsed(Config config)
        {
            _generalConfig = config.General;
            _mapVoteConfig = config.MapVote;
            _rtvConfig = config.Rtv;
        }

        public void OnMapStart(string map)
        {
            Votes.Clear();
            _playerVotes.Clear();
            _currentVoteOptions.Clear();
            _optionItems.Clear();
            _allVotedShortened = false;
            TimeLeft = 0;
            mapsElected.Clear();
            _isRunoff = false;
            KillTimer();
        }

        private void KillChatMapChoiceTimer()
        {
            _chatMapChoiceTimer?.Kill();
            _chatMapChoiceTimer = null;
        }

        private bool ShouldPrintChatMapChoices()
        {
            return string.Equals(_mapVoteConfig.MenuType?.Trim(), "ChatMenu", StringComparison.OrdinalIgnoreCase)
                && _mapVoteConfig.ChatMapChoiceReminder
                && _mapVoteConfig.ChatMapChoiceInterval > 0;
        }

        private void PrintChatMapChoices()
        {
            if (_currentVoteOptions.Count == 0)
                return;

            Server.PrintToChatAll(_localizer.Localize("emv.hud.menu-title"));

            for (int i = 0; i < _currentVoteOptions.Count; i++)
            {
                string option = _currentVoteOptions[i];
                int voteCount = Votes.TryGetValue(option, out int currentVotes) ? currentVotes : 0;
                Server.PrintToChatAll(_localizer.Localize("emv.vote-option", i + 1, voteCount, option));
            }

            if (_mapVoteConfig.EnableRevote)
                Server.PrintToChatAll(_localizer.Localize("emv.revote"));
        }

        private void StartChatMapChoiceReminder()
        {
            KillChatMapChoiceTimer();

            if (_plugin == null || !ShouldPrintChatMapChoices())
                return;

            _chatMapChoiceTimer = _plugin.AddTimer(_mapVoteConfig.ChatMapChoiceInterval, () =>
            {
                if (!_pluginState.MapVoteHappening || TimeLeft <= 0 || !ShouldPrintChatMapChoices())
                {
                    KillChatMapChoiceTimer();
                    return;
                }

                PrintChatMapChoices();
            }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        }

        private void OnRevoteCommand(CCSPlayerController? player, CommandInfo _)
        {
            if (player == null || !player.IsValid)
                return;

            if (!_pluginState.MapVoteHappening || Timer is null || _currentVoteOptions.Count == 0)
                return;

            if (!_mapVoteConfig.EnableRevote)
                return;

            int menuTimeLeft = Math.Max(TimeLeft, 1);
            DisplayVoteMenu(player, _currentVoteOptions, menuTimeLeft, _activeVoteIsRtv, allowRevote: true);
        }

        private void DisplayVoteMenu(CCSPlayerController player, IEnumerable<string> voteOptions, int durationSeconds, bool isRtv, bool allowRevote)
        {
            if (_plugin == null || !player.IsValid || player.UserId == null)
                return;

            var title = _localizer.Localize("emv.hud.menu-title");
            var key = _mapVoteConfig.MenuType?.Trim() ?? "";
            var menuType = MenuManager.MenuTypesList.TryGetValue(key, out var resolvedType)
                ? resolvedType
                : MenuTypeManager.GetDefaultMenu();

            var menu = MenuManager.MenuByType(menuType, title, _plugin);
            if (menu is ChatMenu)
                menu.ExitButton = false;

            foreach (var option in voteOptions)
            {
                var chosen = option;
                int voteCount = Votes.TryGetValue(chosen, out int v) ? v : 0;
                string displayText = $"{chosen} ({voteCount})";

                var item = menu.AddItem(displayText, (p, _) =>
                {
                    MapVoted(p, chosen, isRtv, allowRevote);
                });
                item.PostSelectAction = PostSelectAction.Nothing;

                // Store item reference for live vote count updates (first display only)
                if (!_optionItems.ContainsKey(chosen))
                    _optionItems[chosen] = item;
            }

            menu.Display(player, durationSeconds);
        }

        public void ReopenVoteMenu(CCSPlayerController player)
        {
            if (!_pluginState.MapVoteHappening || Timer is null || _currentVoteOptions.Count == 0)
                return;

            if (player == null || !player.IsValid)
                return;

            int menuTimeLeft = Math.Max(TimeLeft, 1);
            DisplayVoteMenu(player, _currentVoteOptions, menuTimeLeft, _activeVoteIsRtv, allowRevote: _mapVoteConfig.EnableRevote);
        }

        private void MapVoted(CCSPlayerController player, string mapName, bool isRtv, bool allowRevote = false)
        {
            if (!player.IsValid || player.UserId == null)
                return;

            if (!_pluginState.MapVoteHappening || Timer is null)
                return;

            if (!Votes.ContainsKey(mapName))
                return;

            var userId = player.UserId!.Value;
            bool canRevote = _mapVoteConfig.EnableRevote && allowRevote;

            if (_playerVotes.TryGetValue(userId, out var previousMap))
            {
                // Toggle-vote: clicking the same option removes it
                if (string.Equals(previousMap, mapName, StringComparison.OrdinalIgnoreCase))
                {
                    if (Votes.TryGetValue(previousMap, out int prevVotes) && prevVotes > 0)
                        Votes[previousMap] = prevVotes - 1;

                    _playerVotes.Remove(userId);
                    player.PrintToChat(_localizer.LocalizeWithPrefix("emv.vote-removed", mapName));
                    UpdateOptionItemTexts();
                    return;
                }

                if (!canRevote)
                    return;

                if (Votes.TryGetValue(previousMap, out int previousVotes) && previousVotes > 0)
                {
                    Votes[previousMap] = previousVotes - 1;
                }
            }
            _playerVotes[userId] = mapName;
            Votes[mapName] += 1;
            player.PrintToChat(_localizer.LocalizeWithPrefix("emv.you-voted", mapName));
            if (_mapVoteConfig.EnableRevote)
                player.PrintToChat(_localizer.LocalizeWithPrefix("emv.revote"));

            UpdateOptionItemTexts();
        }

        private void UpdateOptionItemTexts()
        {
            foreach (var kvp in _optionItems)
            {
                int voteCount = Votes.TryGetValue(kvp.Key, out int v) ? v : 0;
                kvp.Value.Text = $"{kvp.Key} ({voteCount})";
            }
        }

        public void KillTimer()
        {
            TimeLeft = -1;
            KillChatMapChoiceTimer();
            if (Timer is not null)
            {
                Timer!.Kill();
                Timer = null;
            }
        }

        private static IList<T> Shuffle<T>(Random rng, IList<T> array)
        {
            int n = array.Count;
            while (n > 1)
            {
                int k = rng.Next(n--);
                (array[k], array[n]) = (array[n], array[k]);
            }
            return array;
        }
        
        private void ChatCountdown(int secondsLeft)
        {
            if (!_pluginState.MapVoteHappening)
                return;

            string text = _localizer.LocalizeWithPrefix("general.chat-countdown", secondsLeft);
            foreach (var player in ServerManager.ValidPlayers())
                player.PrintToChat(text);

            int next = secondsLeft - _mapVoteConfig.CountdownInterval;
            if (next > 0)
            {
                _plugin?.AddTimer(
                    _mapVoteConfig.CountdownInterval, () =>
                    {
                        try
                        {
                            ChatCountdown(next);
                        }
                        catch (Exception ex)
                        {
                            _plugin.Logger.LogError($"ChatCountdown timer callback failed: {ex.Message}");
                        }
                    }, TimerFlags.STOP_ON_MAPCHANGE
                );
            }
        }

        public void StartVote(bool isRtv)
        {
            if (_pluginState.MapVoteHappening)
                return;

            _isRunoff = false;
            _playerVotes.Clear();
            _currentVoteOptions.Clear();
            _optionItems.Clear();
            _allVotedShortened = false;
            _activeVoteIsRtv = isRtv;

            Votes.Clear();
            _pluginState.MapVoteHappening = true;

            int mapsToShow = !isRtv
                ? (_mapVoteConfig.MapsToShow == 0 ? 6 : _mapVoteConfig.MapsToShow)
                : (_rtvConfig.MapsToShow == 0 ? 6 : _rtvConfig.MapsToShow);

            // Reserve one slot for "Don't Change Map" in RTV votes
            int mapOptionsCount = isRtv ? mapsToShow - 1 : mapsToShow;

            // Get map list
            var mapsScrambled = Shuffle(new Random(), _mapLister.Maps!.Select(x => x.Name)
                .Where(x => x != Server.MapName).ToList());

            mapsElected = [.. _nominationManager.NominationWinners().Concat(mapsScrambled).Distinct()];

            // Create vote list
            List<string> voteOptions = new();
            foreach (var map in mapsElected.Take(mapOptionsCount))
            {
                Votes[map] = 0;
                voteOptions.Add(map);
            }

            if (isRtv)
            {
                string dontChangeOption = _localizer.Localize("emv.dont-change-map");
                Votes[dontChangeOption] = 0;
                voteOptions.Add(dontChangeOption);
            }

            _currentVoteOptions.AddRange(voteOptions);
            int voteDuration = isRtv ? _rtvConfig.MapVoteDuration : _mapVoteConfig.VoteDuration;
            TimeLeft = voteDuration;

            var players = ServerManager.ValidPlayers()
                .Where(p => p != null && p.IsValid)
                .ToList();

            // Open Menu (config dependant)
            foreach (var player in players)
            {
                DisplayVoteMenu(player, _currentVoteOptions, voteDuration, isRtv, allowRevote: false);
            }

            if (_mapVoteConfig.MenuType != "ChatMenu")
                Server.PrintToChatAll(_localizer.LocalizeWithPrefix("emv.vote-started"));

            StartChatMapChoiceReminder();
            ChatCountdown(voteDuration);

            Timer = _plugin?.AddTimer(1.0F, () =>
            {
                if (TimeLeft <= 0)
                {
                    EndVote(isRtv);
                }
                else
                {
                    // Auto-shorten: when every eligible player has voted, fast-forward to 5s
                    if (!_allVotedShortened && _playerVotes.Count >= ServerManager.ValidPlayerCount())
                    {
                        _allVotedShortened = true;
                        int shortenTo = 5;
                        if (TimeLeft > shortenTo)
                        {
                            TimeLeft = shortenTo;
                            Server.PrintToChatAll(_localizer.LocalizeWithPrefix("emv.vote-ending-soon", shortenTo));
                        }
                    }
                    TimeLeft--;
                }
            }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        }

        public void EndVote(bool isRtv)
        {
            KillTimer();
            _currentVoteOptions.Clear();
            
            string dontChangeOption = _localizer.Localize("emv.dont-change-map");
            
            decimal totalVotes = Votes.Select(x => x.Value).Sum();
            KeyValuePair<string, int> winner;
            Random rnd = new();
            
            if (totalVotes == 0)
            {
                // No votes cast — don't change map, reset state so players can RTV/nominate again
                Server.PrintToChatAll(_localizer.LocalizeWithPrefix("emv.vote-ended-no-votes"));
                _pluginState.MapVoteHappening = false;
                _isRunoff = false;
                VoteEndedNoVotes?.Invoke();
                return;
            }
            else
            {
                int maxVotes = Votes.Values.Max();
                var tiedMaps = Votes.Where(kv => kv.Value == maxVotes).Select(kv => kv.Key).ToList();
                string chosenKey = tiedMaps[rnd.Next(tiedMaps.Count)];
                winner = new KeyValuePair<string,int>(chosenKey, maxVotes);
            }
            
            decimal percent = totalVotes > 0 ? winner.Value / totalVotes * 100M : 0;

            // Check minimum win percentage — trigger runoff if not met
            int minPct = _mapVoteConfig.MinWinPercentage;
            if (minPct > 0 && percent < minPct && !_isRunoff && _mapVoteConfig.RunoffEnabled)
            {
                // Pick top 2 candidates for the runoff (including "Don't Change Map")
                var mapCandidates = Votes
                    .OrderByDescending(kv => kv.Value)
                    .Take(2)
                    .Select(kv => kv.Key)
                    .ToList();

                if (mapCandidates.Count >= 2)
                {
                    Server.PrintToChatAll(_localizer.LocalizeWithPrefix("emv.runoff-started", minPct, mapCandidates.Count));
                    _pluginState.MapVoteHappening = false;
                    StartRunoff(mapCandidates, isRtv);
                    return;
                }
            }
            
            Server.PrintToChatAll(_localizer.LocalizeWithPrefix("emv.vote-ended", winner.Key, percent, totalVotes));
            _isRunoff = false;

            if (winner.Key == dontChangeOption)
            {
                _pluginState.MapVoteHappening = false;
                return;
            }
            else
            {
                _changeMapManager.ScheduleMapChange(winner.Key);

                var delay = _rtvConfig.MapChangeDelay;
                if (delay <= 0)
                {
                    _changeMapManager.ChangeNextMap();
                }
                else
                {
                    _plugin?.AddTimer(delay, () =>
                    {
                        _changeMapManager.ChangeNextMap();
                    }, TimerFlags.STOP_ON_MAPCHANGE);
                }

                _pluginState.MapVoteHappening = false;
            }
        }

        private void StartRunoff(List<string> candidates, bool isRtv)
        {
            _isRunoff = true;
            _playerVotes.Clear();
            _currentVoteOptions.Clear();
            _optionItems.Clear();
            _allVotedShortened = false;
            Votes.Clear();
            _activeVoteIsRtv = isRtv;
            _pluginState.MapVoteHappening = true;

            List<string> voteOptions = [];
            foreach (var map in candidates)
            {
                Votes[map] = 0;
                voteOptions.Add(map);
            }

            if (isRtv)
            {
                string dontChangeOption = _localizer.Localize("emv.dont-change-map");
                if (!voteOptions.Contains(dontChangeOption))
                {
                    Votes[dontChangeOption] = 0;
                    voteOptions.Add(dontChangeOption);
                }
            }

            _currentVoteOptions.AddRange(voteOptions);
            int voteDuration = isRtv ? _rtvConfig.MapVoteDuration : _mapVoteConfig.VoteDuration;
            TimeLeft = voteDuration;

            var players = ServerManager.ValidPlayers()
                .Where(p => p != null && p.IsValid)
                .ToList();

            foreach (var player in players)
            {
                DisplayVoteMenu(player, _currentVoteOptions, voteDuration, isRtv, allowRevote: false);
            }

            if (_mapVoteConfig.MenuType != "ChatMenu")
                Server.PrintToChatAll(_localizer.LocalizeWithPrefix("emv.vote-started"));

            StartChatMapChoiceReminder();
            ChatCountdown(voteDuration);

            Timer = _plugin?.AddTimer(1.0F, () =>
            {
                if (TimeLeft <= 0)
                {
                    EndVote(isRtv);
                }
                else
                {
                    // Auto shorten when every eligible player has voted, fast-forward to 5s
                    if (!_allVotedShortened && _playerVotes.Count >= ServerManager.ValidPlayerCount())
                    {
                        _allVotedShortened = true;
                        int shortenTo = 5;
                        if (TimeLeft > shortenTo)
                        {
                            TimeLeft = shortenTo;
                            Server.PrintToChatAll(_localizer.LocalizeWithPrefix("emv.vote-ending-soon", shortenTo));
                        }
                    }
                    TimeLeft--;
                }
            }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        }
    }
}
