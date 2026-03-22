using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace cs2_rockthevote
{
    public partial class Plugin
    {
        [GameEventHandler(HookMode.Post)]
        public HookResult OnRoundEndMapChanger(EventRoundEnd @event, GameEventInfo info)
        {
            _changeMapManager.ChangeNextMap();
            return HookResult.Continue;
        }

        [GameEventHandler(HookMode.Post)]
        public HookResult OnRoundStartMapChanger(EventRoundStart @event, GameEventInfo info)
        {
            _changeMapManager.ChangeNextMap();
            return HookResult.Continue;
        }
    }

    public class ChangeMapManager : IPluginDependency<Plugin, Config>
    {
        private Plugin? _plugin;
        private StringLocalizer _localizer;
        private PluginState _pluginState;
        private MapLister _mapLister;

        public string? NextMap { get; private set; } = null;
        private string _prefix = DEFAULT_PREFIX;
        private const string DEFAULT_PREFIX = "rtv.prefix";

        public event Action? MapChangeFailed;
        private bool _mapEnd = false;

        private Map[] _maps = [];
        private Config? _config;

        private Timer? _mapChangeVerifyTimer;

        public ChangeMapManager(StringLocalizer localizer, PluginState pluginState, MapLister mapLister)
        {
            _localizer = localizer;
            _pluginState = pluginState;
            _mapLister = mapLister;
            _mapLister.EventMapsLoaded += OnMapsLoaded;
        }

        public void OnMapsLoaded(object? sender, Map[] maps)
        {
            _maps = maps;
        }


        public void ScheduleMapChange(string map, bool mapEnd = false, string prefix = DEFAULT_PREFIX)
        {
            NextMap = map;
            _prefix = prefix;
            _pluginState.MapChangeScheduled = true;
            _mapEnd = mapEnd;
        }

        public void OnMapStart(string _map)
        {
            NextMap = null;
            _prefix = DEFAULT_PREFIX;

            _mapChangeVerifyTimer?.Kill();
            _mapChangeVerifyTimer = null;
        }

        private static string GetBaseMapName(string displayName)
        {
            var idx = displayName.IndexOf(" (", StringComparison.Ordinal);
            return idx >= 0 ? displayName.Substring(0, idx) : displayName;
        }

        public bool ChangeNextMap(bool mapEnd = false)
        {
            if (mapEnd != _mapEnd)
                return false;

            if (!_pluginState.MapChangeScheduled)
                return false;

            Map? map = _maps.FirstOrDefault(x => string.Equals(x.Name, NextMap, StringComparison.OrdinalIgnoreCase));
            if (map == null)
            {
                Server.PrintToChatAll(_localizer.LocalizeWithPrefixInternal(_prefix, "general.map-resolve-failed", NextMap ?? ""));
                return false;
            }

            _pluginState.MapChangeScheduled = false;

            Server.PrintToChatAll(_localizer.LocalizeWithPrefixInternal(_prefix, "general.changing-map", map.Name));

            string mapBefore = Server.MapName ?? string.Empty;
            // Strip annotation (e.g. " (T3)") so engine commands get a clean map name
            string cleanName = GetBaseMapName(map.Name);


            _plugin?.AddTimer(3.0F, () =>
            {
                try
                {
                    if (Server.IsMapValid(cleanName))
                    {
                        Server.ExecuteCommand($"changelevel {cleanName}");
                    }
                    else if (map.Id is not null)
                    {
                        Server.ExecuteCommand($"host_workshop_map {map.Id}");
                    }
                    else
                    {
                        Server.ExecuteCommand($"ds_workshop_changelevel {cleanName}");
                    }
                }
                catch (Exception ex)
                {
                    Server.PrintToConsole($"[RTV] Map change command failed: {ex.Message}");
                }

                // Create 30s verification timer - log debug info if map change failed
                _mapChangeVerifyTimer?.Kill();
                _mapChangeVerifyTimer = _plugin?.AddTimer(30.0F, () =>
                {
                    try
                    {
                        string current = Server.MapName ?? string.Empty;

                        if (string.Equals(current, mapBefore, StringComparison.OrdinalIgnoreCase))
                        {
                            Server.PrintToConsole($"[RTV] Map change to '{map.Name}' failed - still on '{current}' after 30s.");
                            Server.PrintToConsole($"[RTV] Map details: Name='{map.Name}', Id='{map.Id}', IsMapValid={Server.IsMapValid(cleanName)}");
                            Server.PrintToConsole($"[RTV] Available maps in maplist: {string.Join(", ", _maps.Select(m => m.Name))}");

                            Server.PrintToChatAll(_localizer.LocalizeWithPrefixInternal(_prefix, "general.map-change-failed", map.Name));
                            NextMap = null;
                            MapChangeFailed?.Invoke();
                        }
                    }
                    catch (Exception ex)
                    {
                        Server.PrintToConsole($"[RTV] Map change verification error: {ex.Message}");
                    }
                }, TimerFlags.STOP_ON_MAPCHANGE); // auto-kill if the map did change
            });

            return true;
        }

        public void OnConfigParsed(Config config)
        {
            _config = config;
        }

        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin;
            plugin.RegisterEventHandler<EventCsWinPanelMatch>((ev, info) =>
            {
                if (_pluginState.MapChangeScheduled)
                {
                    ChangeNextMap(true);
                }
                return HookResult.Continue;
            });
        }
    }
}
