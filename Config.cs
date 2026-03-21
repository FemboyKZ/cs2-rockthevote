using CounterStrikeSharp.API.Core;
using System.Text.Json.Serialization;

namespace cs2_rockthevote
{
    public class RtvConfig
    {
        public bool Enabled { get; set; } = true;
        public int MapChangeDelay { get; set; } = 5;
        public int MapsToShow { get; set; } = 6;
        public int ReminderInterval { get; set; } = 60;
        public int MapVoteDuration { get; set; } = 60;
        public int CooldownDuration { get; set; } = 30;
        public int MapStartDelay { get; set; } = 30;
        public int VotePercentage { get; set; } = 51;
    }

    public class MapVoteConfig
    {
        public bool Enabled { get; set; } = true;
        public bool EnableRevote {get; set; } = true;
        public int MapsToShow { get; set; } = 6;
        public string MenuType { get; set; } = "ChatMenu";
        public int VoteDuration { get; set; } = 90;
        public int CountdownInterval { get; set; } = 15;
        public bool ChatMapChoiceReminder { get; set; } = true;
        public int ChatMapChoiceInterval { get; set; } = 15;
        public int MinWinPercentage { get; set; } = 0;
        public bool RunoffEnabled { get; set; } = true;
    }

    public class NominateConfig
    {
        public bool Enabled { get; set; } = true;
        public string MenuType { get; set; } = "ChatMenu";
        public int NominateLimit { get; set; } = 1;
        public string Permission { get; set; } = "";
    }

    public class MapChooserConfig
    {
        public string Command { get; set; } = "mapmenu,mm";
        public string MenuType { get; set; } = "ChatMenu";
        public string Permission { get; set; } = "@css/changemap";
    }

    public class GeneralConfig
    {
        public string AdminPermission { get; set; } = "@css/root";
        public bool IncludeSpectator { get; set; } = true;
        public bool EnableMapValidation { get; set; } = true;
        public string SteamApiKey { get; set; } = "";
        public string DiscordWebhook { get; set; } = "";
    }

    public class Config : BasePluginConfig, IBasePluginConfig
    {
        public const int CurrentVersion = 25;

        [JsonPropertyName("ConfigVersion")]
        public override int Version { get; set; } = CurrentVersion;
        public RtvConfig Rtv { get; set; } = new();
        public MapVoteConfig MapVote { get; set; } = new();
        public NominateConfig Nominate { get; set; } = new();
        public MapChooserConfig MapChooser { get; set; } = new();
        public GeneralConfig General { get; set; } = new();
    }
}
