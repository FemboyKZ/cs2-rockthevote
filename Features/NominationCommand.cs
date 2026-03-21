using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CS2MenuManager.API.Menu;
using CS2MenuManager.API.Class;
using CS2MenuManager.API.Enum;
using Microsoft.Extensions.Logging;

namespace cs2_rockthevote
{
    public partial class Plugin
    {
        [ConsoleCommand("css_nom", "Nominate a map to appear in the vote.")]
        [ConsoleCommand("css_nominate", "Nominate a map to appear in the vote.")]
        [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
        public void OnNominateCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null)
                return;
            
            // If "Permission" is blank or whitespace, allow everyone. Otherwise enforce it
            string perm = Config.Nominate.Permission;
            bool hasPerm = string.IsNullOrWhiteSpace(perm) || AdminManager.PlayerHasPermissions(player, perm);

            if (!hasPerm)
            {
                command.ReplyToCommand(_localizer.LocalizeWithPrefix("general.incorrect.permission"));
                return;
            }

            _nominationManager.CommandHandler(player, command.GetArg(1)?.Trim().ToLower() ?? "");
        }

        [GameEventHandler(HookMode.Pre)]
        public HookResult EventPlayerDisconnectNominate(EventPlayerDisconnect @event, GameEventInfo @eventInfo)
        {
            var player = @event.Userid;
            if (player != null)
            {
                _nominationManager.PlayerDisconnected(player);
            }
            return HookResult.Continue;
        }
    }

    public class NominationCommand : IPluginDependency<Plugin, Config>
    {
        private readonly ILogger<NominationCommand> _logger;
        Dictionary<int, List<string>> Nominations = new();
        private ChatMenu? _nominationMenu;
        private NominateConfig _nomConfig = new();
        private StringLocalizer _localizer;
        private PluginState _pluginState;
        private MapLister _mapLister;
        private Plugin? _plugin;

        public NominationCommand(MapLister mapLister, ChangeMapManager changeMapManager, StringLocalizer localizer, PluginState pluginState, ILogger<NominationCommand> logger)
        {
            _mapLister = mapLister;
            _mapLister.EventMapsLoaded += OnMapsLoaded;
            _localizer = localizer;
            _pluginState = pluginState;
            _logger = logger;
            changeMapManager.MapChangeFailed += OnMapChangeFailed;
        }

        public void OnVoteEndedNoVotes()
        {
            Nominations.Clear();
        }

        private void OnMapChangeFailed()
        {
            Nominations.Clear();
        }

        public void OnMapStart(string map)
        {
            Nominations.Clear();
        }
        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin;
        }

        public void OnConfigParsed(Config config)
        {
            _nomConfig = config.Nominate;
        }

        public void OnMapsLoaded(object? sender, Map[] maps)
        {
            _nominationMenu = new ChatMenu(_localizer.Localize("nominate.title"), _plugin!);

            foreach (var map in _mapLister.Maps!.Where(x => !GetBaseMapName(x.Name).Equals(Server.MapName, StringComparison.OrdinalIgnoreCase)))
            {
                _nominationMenu.AddItem(map.Name, (player, _) =>
                {
                    Nominate(player, map.Name);
                });
            }
        }

        public void CommandHandler(CCSPlayerController? player, string map)
        {
            if (player == null)
                return;

            if (!_nomConfig.Enabled || _pluginState.MapVoteHappening)
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.disabled"));
                return;
            }
            
            int userId = player.UserId!.Value;
            int existingCount = Nominations.TryGetValue(userId, out var userNoms) ? userNoms.Count : 0;
            if (_nomConfig.NominateLimit > 0 && existingCount >= _nomConfig.NominateLimit)
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("nominate.limit", _nomConfig.NominateLimit));
                return;
            }

            var mapName = map.Trim();

            if (string.IsNullOrEmpty(mapName))
            {
                var title = _localizer.Localize("nominate.title");
                var key = _nomConfig.MenuType?.Trim() ?? "";
                var menuType = MenuManager.MenuTypesList.TryGetValue(key, out var resolvedType)
                    ? resolvedType
                    : MenuTypeManager.GetDefaultMenu();

                var menu = MenuManager.MenuByType(menuType, title, _plugin!);

                foreach (var m in _mapLister.Maps!
                            .Where(x => !GetBaseMapName(x.Name).Equals(Server.MapName, StringComparison.OrdinalIgnoreCase)))
                {
                    string chosen = m.Name;

                    menu.AddItem(m.Name, (p, _) =>
                    {
                        Nominate(p, chosen);
                    });
                }

                menu.Display(player, 0);
                return;
            }

            // Check if input looks like a workshop ID (all digits, 8+ chars)
            if (mapName.All(char.IsDigit) && mapName.Length >= 8)
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("nominate.workshop-looking-up"));
                int slot = player.Slot;
                _ = Task.Run(async () =>
                {
                    var result = await _mapLister.LookupByWorkshopIdAsync(mapName);
                    Server.NextWorldUpdate(() =>
                    {
                        var p = Utilities.GetPlayerFromSlot(slot);
                        if (p == null || !p.IsValid) return;

                        if (result == null)
                        {
                            p.PrintToChat(_localizer.LocalizeWithPrefix("nominate.workshop-not-found"));
                            return;
                        }

                        _mapLister.AddDynamicMap(result);
                        Nominate(p, result.Name);
                    });
                });
                return;
            }

            // Try local maplist first
            var resolved = ResolveMapNameLocal(player, mapName, out bool menuShown);
            if (resolved != null)
            {
                Nominate(player, resolved);
                return;
            }

            // If a multi-match menu was already shown, don't fall through to API
            if (menuShown)
                return;

            // No local match, try CS2KZ API
            player.PrintToChat(_localizer.LocalizeWithPrefix("nominate.searching-api"));
            int playerSlot = player.Slot;
            _ = Task.Run(async () =>
            {
                var apiResults = await _mapLister.LookupByNameAsync(mapName);
                Server.NextWorldUpdate(() =>
                {
                    var p = Utilities.GetPlayerFromSlot(playerSlot);
                    if (p == null || !p.IsValid) return;

                    if (apiResults.Count == 0)
                    {
                        p.PrintToChat(_localizer.LocalizeWithPrefix("general.invalid-map"));
                        return;
                    }

                    if (apiResults.Count == 1)
                    {
                        _mapLister.AddDynamicMap(apiResults[0]);
                        Nominate(p, apiResults[0].Name);
                        return;
                    }

                    // Multiple matches, show picker menu
                    var menu = new ChatMenu(_localizer.Localize("nominate.multiple-maps"), _plugin!);
                    foreach (var m in apiResults)
                    {
                        var entry = m;
                        menu.AddItem(m.Name, (mp, _) =>
                        {
                            _mapLister.AddDynamicMap(entry);
                            Nominate(mp, entry.Name);
                        });
                    }
                    menu.Display(p, 0);
                });
            });
        }

        public void Nominate(CCSPlayerController player, string map)
        {
            var userId  = player.UserId!.Value;
            var mapName = map.Trim();
            var baseName = GetBaseMapName(mapName);

            // Ensure per-player list exists
            if (!Nominations.TryGetValue(userId, out var userNoms))
            {
                userNoms = new List<string>();
                Nominations[userId] = userNoms;
            }

            // Enforce per-player nomination limit
            if (userNoms.Count >= _nomConfig.NominateLimit)
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("nominate.limit", _nomConfig.NominateLimit));
                return;
            }

            // Prevent nominating the same map multiple times by this player
            if (userNoms.Contains(mapName, StringComparer.OrdinalIgnoreCase))
            {
                int voteCount = Nominations.Values
                    .SelectMany(v => v)
                    .Count(m => m.Equals(mapName, StringComparison.OrdinalIgnoreCase));

                player.PrintToChat(_localizer.LocalizeWithPrefix("nominate.already-nominated", mapName, voteCount));
                return;
            }

            // Can't nominate the current map
            if (baseName.Equals(Server.MapName, StringComparison.OrdinalIgnoreCase))
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.current-map"));
                return;
            }

            // All validations passed, record the nomination now
            userNoms.Add(mapName);

            int totalVotes = Nominations.Values
                .SelectMany(v => v)
                .Count(m => m.Equals(mapName, StringComparison.OrdinalIgnoreCase));

            Server.PrintToChatAll(_localizer.LocalizeWithPrefix("nominate.nominated", player.PlayerName, mapName, totalVotes));
        }

        private void ShowMultipleMatchesMenu(CCSPlayerController player, List<string> matchingMaps)
        {
            var menu = new ChatMenu(_localizer.Localize("nominate.multiple-maps"), _plugin!);

            foreach (var name in matchingMaps)
            {
                menu.AddItem(name, (p, _) => Nominate(p, name));
            }

            menu.Display(player, 0);
        }

        // Try to resolve user input against the local maplist only.
        // Returns null if no match (caller should try API fallback).
        // Sets menuShown=true if a multi-match menu was displayed.
        private string? ResolveMapNameLocal(CCSPlayerController player, string input, out bool menuShown)
        {
            menuShown = false;

            var exact = _mapLister.GetExactMapName(input);
            if (exact is not null)
                return exact;

            var matches = _mapLister.GetMatchingMapNames(input);

            if (matches.Count == 0)
                return null;

            if (matches.Count > 1)
            {
                ShowMultipleMatchesMenu(player, matches);
                menuShown = true;
                return null;
            }

            return matches[0];
        }

        private string GetBaseMapName(string displayName)
        {
            var idx = displayName.IndexOf(" (", StringComparison.Ordinal);
            return idx >= 0
                ? displayName.Substring(0, idx)
                : displayName;
        }

        public List<string> NominationWinners()
        {
            if (Nominations.Count == 0)
                return new List<string>();

            var rawNominations = Nominations
                .Select(x => x.Value)
                .Aggregate((acc, x) => acc.Concat(x).ToList());

            return [.. rawNominations
                .Distinct()
                .Select(map => new KeyValuePair<string, int>(map, rawNominations.Count(x => x == map)))
                .OrderByDescending(x => x.Value)
                .Select(x => x.Key)];
        }

        public void PlayerDisconnected(CCSPlayerController player)
        {
            int userId = player.UserId!.Value;
            Nominations.Remove(userId);
        }
    }
}
