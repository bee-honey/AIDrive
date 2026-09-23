using System;

namespace AIDrive.Scenario
{
    /// <summary>One prop (or a row of them) placed by road address. JSON contract for scenario files.</summary>
    [Serializable]
    public class PropPlacement
    {
        /// <summary>Cone, Barrier, RoadClosed, JerseyBarrier, ParkedCar.</summary>
        public string type;
        public string street;
        /// <summary>The two cross streets bounding the block, e.g. ["S3", "S4"].</summary>
        public string[] between;
        /// <summary>eastbound / westbound / northbound / southbound.</summary>
        public string direction;
        /// <summary>inner / curb / both.</summary>
        public string lane;
        /// <summary>0 … 1 along the block in the direction of travel.</summary>
        public float at;
        /// <summary>Number of props in a row along the lane (default 1).</summary>
        public int count;
        /// <summary>Metres between props in a row (default 4).</summary>
        public float spacing;
    }

    /// <summary>A reproducible test drive: where to start, where to go, what's on the road, what should happen.</summary>
    [Serializable]
    public class ScenarioDef
    {
        public string name;
        public string description;
        public string start;
        public string destination;
        /// <summary>Expected outcome: Arrived or Blocked.</summary>
        public string expect;
        public int min_lane_changes;
        public float timeout;
        public PropPlacement[] props;

        public string Expect => string.IsNullOrEmpty(expect) ? "Arrived" : expect;
        public float Timeout => timeout > 0f ? timeout : 180f;
    }

    [Serializable]
    public class ScenarioResult
    {
        public string scenario;
        public string expected;
        public string outcome;
        public bool passed;
        public string failure;
        public double time_s;
        public double distance_m;
        public double route_m;
        public int collisions;
        public int lane_changes;
        public double max_lane_error_m;
        public int red_light_stops;
        public int red_light_violations;
        public double time_at_lights_s;
        public string blocked_by;
        public string blocked_at;
    }
}
