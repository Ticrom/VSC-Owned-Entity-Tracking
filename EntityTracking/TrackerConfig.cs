using System.Collections.Generic;

namespace EntityTracking
{
    public class TrackerConfig
    {
        public string TrackCommandPrivilege { get; set; } = "chat";
        public int UpdateIntervalSeconds { get; set; } = 300;
        public List<string> TrackedEntityTypes { get; set; } = new List<string>
        {
            "sailboat", "boat", "raft", "canoe",
            "wolf", "hyena", "aurochs", "moose", "bighorn",
            "sawtooth", "tameddeer", "elk", "deer", "horse"
        };
    }
}
