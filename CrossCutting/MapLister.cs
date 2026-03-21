using CounterStrikeSharp.API;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace cs2_rockthevote
{
    public class MapLister : IPluginDependency<Plugin, Config>
    {
        public Map[]? Maps { get; private set; } = null;
        public bool MapsLoaded { get; private set; } = false;
        public event EventHandler<Map[]>? EventMapsLoaded;
        private Plugin? _plugin;
        private readonly ILogger<MapLister> _logger;
        private static readonly HttpClient _httpClient = new();

        private static readonly string[] DefaultCompetitiveMaps =
        [
            "ar_baggage",
            "ar_shoots",
            "cs_italy",
            "cs_office",
            "de_ancient",
            "de_anubis",
            "de_dust2",
            "de_inferno",
            "de_mirage",
            "de_nuke",
            "de_overpass",
            "de_train",
            "de_vertigo"
        ];

        private static readonly Dictionary<string, string> TierMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["very-easy"] = "T1",
            ["easy"] = "T2",
            ["medium"] = "T3",
            ["advanced"] = "T4",
            ["hard"] = "T5",
            ["very-hard"] = "T6",
            ["extreme"] = "T7",
            ["death"] = "T8"
        };

        private string _kzTierMode = "classic";

        public MapLister(ILogger<MapLister> logger)
        {
            _logger = logger;
        }

        public void OnConfigParsed(Config config)
        {
            _kzTierMode = config.General.KzTierMode;
        }

        private static string GetMaplistPath()
        {
            return Path.Combine(Server.GameDirectory, "csgo", "cfg", "maplist.txt");
        }

        public void Clear()
        {
            MapsLoaded = false;
            Maps = null;
        }

        public void LoadMaps()
        {
            Clear();
            string mapsFile = GetMaplistPath();

            if (!File.Exists(mapsFile))
            {
                _logger.LogInformation("[RTV] maplist.txt not found at {Path}, generating from CS2KZ API...", mapsFile);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await GenerateMaplistAsync(mapsFile);
                        Server.NextWorldUpdate(() => LoadMaps());
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[RTV] Failed to generate maplist from CS2KZ API");
                    }
                });
                return;
            }

            Maps = [.. File.ReadAllText(mapsFile)
                .Replace("\r\n", "\n")
                .Split("\n")
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x) && !x.StartsWith("//"))
                .Select(mapLine =>
                {
                    string[] args = mapLine.Split(":");
                    string mapName = args[0];
                    string? mapValue = args.Length == 2 ? args[1] : null;
                    return new Map(mapName, mapValue);
                })];

            MapsLoaded = true;
            EventMapsLoaded?.Invoke(this, Maps!);
        }

        private async Task GenerateMaplistAsync(string mapsFile)
        {
            var lines = new List<string>
            {
                "// Auto-generated maplist - edit freely, this file will not be overwritten",
                "// Format: mapname:workshopid  or  mapname (T1):workshopid",
                "",
                "// Default competitive maps"
            };

            foreach (var map in DefaultCompetitiveMaps)
                lines.Add(map);

            lines.Add("");
            lines.Add("// KZ maps (fetched from CS2KZ API)");

            int offset = 0;
            const int limit = 500;
            int total;

            do
            {
                string url = $"https://api.cs2kz.org/maps?state=approved&limit={limit}&offset={offset}";
                string json = await _httpClient.GetStringAsync(url);

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                total = root.GetProperty("total").GetInt32();

                foreach (var mapEl in root.GetProperty("values").EnumerateArray())
                {
                    string name = mapEl.GetProperty("name").GetString() ?? "";
                    ulong workshopId = mapEl.GetProperty("workshop_id").GetUInt64();

                    string tier = "T?";
                    if (mapEl.TryGetProperty("courses", out var courses))
                    {
                        var firstCourse = courses.EnumerateArray().FirstOrDefault();
                        if (firstCourse.ValueKind != JsonValueKind.Undefined
                            && firstCourse.TryGetProperty("filters", out var filters)
                            && filters.TryGetProperty(_kzTierMode, out var modeFilters)
                            && modeFilters.TryGetProperty("nub_tier", out var nubTier))
                        {
                            string tierStr = nubTier.GetString() ?? "";
                            tier = TierMap.TryGetValue(tierStr, out var mapped) ? mapped : tierStr;
                        }
                    }

                    lines.Add($"{name} ({tier}):{workshopId}");
                }

                offset += limit;
            } while (offset < total);

            Directory.CreateDirectory(Path.GetDirectoryName(mapsFile)!);
            await File.WriteAllTextAsync(mapsFile, string.Join("\n", lines));

            int mapCount = lines.Count(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("//"));
            _logger.LogInformation("[RTV] Generated maplist.txt with {Count} maps at {Path}", mapCount, mapsFile);
        }

        public void OnMapStart(string _map)
        {
            if (_plugin is not null)
                LoadMaps();
        }

        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin;
            LoadMaps();
        }

        public string? GetExactMapName(string name)
        {
            if (Maps == null) return null;
            return Maps
                .Select(m => m.Name)
                .FirstOrDefault(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        public List<string> GetMatchingMapNames(string partial)
        {
            if (Maps == null) return new List<string>();
            return [.. Maps
                .Select(m => m.Name)
                .Where(n => n.Contains(partial, StringComparison.OrdinalIgnoreCase))];
        }

        public void PruneMaps(IEnumerable<Map> toRemove)
        {
            if (Maps is null) return;
            Maps = [.. Maps.Where(m => !toRemove.Contains(m))];
        }

        // Add a map that isn't on the maplist (e.g. nominated by workshop ID).
        // It will persist in memory until the next map load.
        public void AddDynamicMap(Map map)
        {
            if (Maps is null) return;
            string baseName = GetBaseMapName(map.Name);
            if (Maps.Any(m => GetBaseMapName(m.Name).Equals(baseName, StringComparison.OrdinalIgnoreCase)))
                return;
            Maps = [.. Maps, map];
        }

        private static string GetBaseMapName(string displayName)
        {
            var idx = displayName.IndexOf(" (", StringComparison.Ordinal);
            return idx >= 0 ? displayName.Substring(0, idx) : displayName;
        }

        // Look up a map by workshop ID. Tries CS2KZ API first, then Steam Workshop.
        public async Task<Map?> LookupByWorkshopIdAsync(string workshopId)
        {
            // Try CS2KZ API first (has tier info)
            try
            {
                string url = $"https://api.cs2kz.org/maps?workshop_id={workshopId}&limit=1";
                string json = await _httpClient.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.GetProperty("total").GetInt32() > 0)
                {
                    var mapEl = root.GetProperty("values").EnumerateArray().First();
                    string name = mapEl.GetProperty("name").GetString() ?? "";
                    ulong wid = mapEl.GetProperty("workshop_id").GetUInt64();
                    string tier = ResolveTier(mapEl);
                    return new Map($"{name} ({tier})", wid.ToString());
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RTV] CS2KZ API lookup by workshop ID failed");
            }

            // Fall back to Steam Workshop
            return await LookupByWorkshopIdSteamAsync(workshopId);
        }

        // Look up a map on the Steam Workshop by published file ID.
        private async Task<Map?> LookupByWorkshopIdSteamAsync(string workshopId)
        {
            try
            {
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("itemcount", "1"),
                    new KeyValuePair<string, string>("publishedfileids[0]", workshopId)
                });

                var response = await _httpClient.PostAsync(
                    "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/",
                    content);
                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                var details = doc.RootElement
                    .GetProperty("response")
                    .GetProperty("publishedfiledetails")[0];

                if (details.GetProperty("result").GetInt32() != 1)
                    return null;

                string title = details.GetProperty("title").GetString() ?? "";
                if (string.IsNullOrWhiteSpace(title))
                    return null;

                return new Map(title, workshopId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RTV] Steam Workshop lookup by ID failed");
                return null;
            }
        }

        // Look up a map on the CS2KZ API by name. Returns null if not found, or multiple if ambiguous.
        public async Task<List<Map>> LookupByNameAsync(string name)
        {
            try
            {
                string url = $"https://api.cs2kz.org/maps?name={Uri.EscapeDataString(name)}&state=approved&limit=5";
                string json = await _httpClient.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var results = new List<Map>();
                foreach (var mapEl in root.GetProperty("values").EnumerateArray())
                {
                    string mapName = mapEl.GetProperty("name").GetString() ?? "";
                    ulong wid = mapEl.GetProperty("workshop_id").GetUInt64();
                    string tier = ResolveTier(mapEl);
                    results.Add(new Map($"{mapName} ({tier})", wid.ToString()));
                }
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RTV] CS2KZ API lookup by name failed");
                return [];
            }
        }

        private string ResolveTier(JsonElement mapEl)
        {
            if (mapEl.TryGetProperty("courses", out var courses))
            {
                var firstCourse = courses.EnumerateArray().FirstOrDefault();
                if (firstCourse.ValueKind != JsonValueKind.Undefined
                    && firstCourse.TryGetProperty("filters", out var filters)
                    && filters.TryGetProperty(_kzTierMode, out var modeFilters)
                    && modeFilters.TryGetProperty("nub_tier", out var nubTier))
                {
                    string tierStr = nubTier.GetString() ?? "";
                    return TierMap.TryGetValue(tierStr, out var mapped) ? mapped : tierStr;
                }
            }
            return "T?";
        }
    }
}
