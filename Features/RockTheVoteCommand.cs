using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;
using Microsoft.Extensions.Logging;

namespace cs2_rockthevote
{
    public partial class Plugin
    {
        [ConsoleCommand("css_rtv", "Votes to rock the vote")]
        [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
        public void OnRTV(CCSPlayerController? player, CommandInfo? command)
        {
            if (player == null)
                return;
            
            _rtvManager.CommandHandler(player);
        }

        [GameEventHandler(HookMode.Pre)]
        public HookResult EventPlayerDisconnectRTV(EventPlayerDisconnect @event, GameEventInfo @eventInfo)
        {
            var player = @event.Userid;
            if (player != null)
            {
                _rtvManager.PlayerDisconnected(player);
            }
            return HookResult.Continue;
        }
    }

    public class RockTheVoteCommand : IPluginDependency<Plugin, Config>
    {
        private readonly ILogger<RockTheVoteCommand> _logger;
        private readonly StringLocalizer _localizer;
        private readonly MapVoteManager _mapVoteManager;
        private readonly PluginState _pluginState;
        private RtvConfig _config = new();
        private GeneralConfig _generalConfig = new();
        private AsyncVoteManager? _voteManager;
        private Plugin? _plugin;
        private Timer? _reminderTimer;

        public RockTheVoteCommand(MapVoteManager mapVoteManager, ChangeMapManager changeMapManager, StringLocalizer localizer, PluginState pluginState, ILogger<RockTheVoteCommand> logger)
        {
            _localizer = localizer;
            _mapVoteManager = mapVoteManager;
            _pluginState = pluginState;
            _logger = logger;
            changeMapManager.MapChangeFailed += OnMapChangeFailed;
            mapVoteManager.VoteEndedNoVotes += OnVoteEndedNoVotes;
        }

        private void OnVoteEndedNoVotes()
        {
            _voteManager?.OnMapStart(string.Empty);
            StopReminderTimer();
        }

        private void OnMapChangeFailed()
        {
            _voteManager?.OnMapStart(string.Empty);
            StopReminderTimer();
        }

        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin;
        }

        public void OnConfigParsed(Config config)
        {
            _config = config.Rtv;
            _generalConfig = config.General;
            _voteManager = new AsyncVoteManager(_config.VotePercentage);
        }

        public void OnMapStart(string map)
        {
            _voteManager?.OnMapStart(map);
            StopReminderTimer();
        }

        private IEnumerable<CCSPlayerController> EligiblePlayers()
        {
            var players = ServerManager.ValidPlayers().Where(p => p.ReallyValid());

            if (!_generalConfig.IncludeSpectator)
                players = players.Where(p => p.Team != CsTeam.Spectator);

            return players;
        }

        private int EligibleCount()
        {
            return EligiblePlayers().Count();
        }

        private static string Tag(CCSPlayerController p)
            => $"{p.PlayerName} [slot {p.Slot}]";

        private int RequiredYesVotes(int eligiblePlayers)
        {
            return (int)Math.Ceiling(eligiblePlayers * (_config.VotePercentage / 100.0));
        }

        public void CommandHandler(CCSPlayerController? player)
        {
            try
            {
                if (player == null)
                    return;

                if (!_config.Enabled || _pluginState.MapChangeScheduled)
                {
                    player.PrintToChat(_localizer.LocalizeWithPrefix("rtv.disabled"));
                    return;
                }

                if (_pluginState.MapVoteHappening)
                {
                    _mapVoteManager.ReopenVoteMenu(player);
                    return;
                }
                Server.PrintToConsole($"[RockTheVote] RTV starting (caller: {Tag(player)})");

                if (!_generalConfig.IncludeSpectator && player.Team == CsTeam.Spectator)
                {
                    player.PrintToChat($"{_localizer.LocalizeWithPrefix("general.spectator")}");
                    return;
                }

                int eligible = Math.Max(EligibleCount(), 0);
                int requiredYesVotes = RequiredYesVotes(eligible);

                VoteResult result = _voteManager!.AddVote(player.UserId!.Value, eligible);

                switch (result.Result)
                {
                    case VoteResultEnum.Added:
                        StartReminderTimer();
                        Server.PrintToChatAll($"{_localizer.LocalizeWithPrefix("rtv.rocked-the-vote", player.PlayerName)} {_localizer.Localize("general.votes-needed", result.VoteCount, requiredYesVotes)}");
                        Server.PrintToChatAll($"{_localizer.LocalizeWithPrefix("rtv.instructions")}");
                        if (result.VoteCount >= requiredYesVotes)
                        {
                            StopReminderTimer();
                            _mapVoteManager.StartVote(isRtv: true);
                            Server.PrintToChatAll(_localizer.LocalizeWithPrefix("rtv.votes-reached"));
                        }
                        break;

                    case VoteResultEnum.AlreadyAddedBefore:
                        player.PrintToChat($"{_localizer.LocalizeWithPrefix("rtv.already-rocked-the-vote")} {_localizer.Localize("general.votes-needed", result.VoteCount, requiredYesVotes)}");
                        if (result.VoteCount >= requiredYesVotes)
                        {
                            StopReminderTimer();
                            _mapVoteManager.StartVote(isRtv: true);
                            Server.PrintToChatAll(_localizer.LocalizeWithPrefix("rtv.votes-reached"));
                        }
                        break;

                    case VoteResultEnum.VotesAlreadyReached:
                        StopReminderTimer();
                        player.PrintToChat(_localizer.LocalizeWithPrefix("rtv.disabled"));
                        break;

                    case VoteResultEnum.VotesReached:
                        StopReminderTimer();
                        _mapVoteManager.StartVote(isRtv: true);
                        Server.PrintToChatAll($"{_localizer.LocalizeWithPrefix("rtv.rocked-the-vote", player.PlayerName)} {_localizer.Localize("general.votes-needed", result.VoteCount, requiredYesVotes)}");
                        Server.PrintToChatAll(_localizer.LocalizeWithPrefix("rtv.votes-reached"));
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Something went wrong with the rtv command: {Message}", ex.Message);
            }
        }

        private void StartReminderTimer()
        {
            if (_reminderTimer != null)
                return;

            if (_pluginState.MapChangeScheduled || _pluginState.MapVoteHappening)
                return;

            if (_config.ReminderInterval <= 0)
                return;

            _reminderTimer = _plugin?.AddTimer(
                _config.ReminderInterval, () =>
                {
                    try
                    {
                        SendReminder();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "RTV reminder failed: {Message}", ex.Message);
                    }
                },
                TimerFlags.STOP_ON_MAPCHANGE | TimerFlags.REPEAT
            );
        }

        private void SendReminder()
        {
            if (_voteManager == null)
            {
                StopReminderTimer();
                return;
            }

            if (_pluginState.MapChangeScheduled || _pluginState.MapVoteHappening)
            {
                StopReminderTimer();
                return;
            }

            if (_voteManager.VotesAlreadyReached)
            {
                StopReminderTimer();
                return;
            }

            int eligible = Math.Max(EligibleCount(), 0);
            int requiredYesVotes = RequiredYesVotes(eligible);
            int remaining = Math.Max(requiredYesVotes - _voteManager.VoteCount, 0);

            if (remaining <= 0)
            {
                StopReminderTimer();
                return;
            }

            Server.PrintToChatAll(_localizer.LocalizeWithPrefix("rtv.in-progress", remaining));
        }

        private void StopReminderTimer()
        {
            _reminderTimer?.Kill();
            _reminderTimer = null;
        }

        public void PlayerDisconnected(CCSPlayerController? player)
        {
            if (player?.UserId == null)
                return;

            _voteManager?.RemoveVote(player.UserId.Value);
        }
    }
}
