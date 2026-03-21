namespace cs2_rockthevote
{
    public class PluginState : IPluginDependency<Plugin, Config>
    {
        public bool MapChangeScheduled { get; set; }
        public bool MapVoteHappening { get; set; }

        public void OnMapStart(string map)
        {
            MapChangeScheduled = false;
            MapVoteHappening = false;
        }
    }
}
