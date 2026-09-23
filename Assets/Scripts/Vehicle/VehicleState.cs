using System;
using System.Linq;
using UnityEngine;

namespace AIDrive.Vehicle
{
    [Serializable]
    public class BlockInfo
    {
        public string street;
        public string between;
        public string location;
        public double distance_m;
        public string by;
        public double x;
        public double z;
    }

    [Serializable]
    public class SensorState
    {
        public string name;
        public double angle;
        public double range;
        public double distance;
        public string hit;
    }

    /// <summary>
    /// Snapshot of everything the agent may need to know about the vehicle. Field names are the JSON contract
    /// for the future <c>get_vehicle_state()</c> tool, hence snake_case.
    /// </summary>
    [Serializable]
    public class VehicleState
    {
        public string state;
        public double speed_kmh;
        public string location;
        public string heading;
        public string destination;
        public string route;
        public string next;
        public double distance_remaining_m;
        public double path_obstacle_m;
        public double front_clearance_m;
        public double left_clearance_m;
        public double right_clearance_m;
        public double rear_clearance_m;
        public double lane_shift_m;
        public string next_signal;
        public int red_light_stops;
        public int red_light_violations;
        public double time_at_lights_s;
        public bool is_blocked;
        public BlockInfo blocked;
        public int collisions;
        public SensorState[] sensors;

        public string ToJson(bool pretty = true) => JsonUtility.ToJson(this, pretty);

        public static VehicleState Capture(Autopilot ap)
        {
            var car = ap.Vehicle;
            var loc = ap.Locate();
            var sensors = ap.Sensors;

            double Clearance(string ray) => sensors != null && sensors.TryGet(ray, out var r) ? Round(r.Distance) : -1;

            return new VehicleState
            {
                state = ap.CurrentState.ToString(),
                speed_kmh = Round(car.Speed * 3.6f),
                location = loc.Description,
                heading = loc.Heading,
                destination = ap.Destination ?? "",
                route = ap.Route?.Summary ?? "",
                next = ap.NextInstruction ?? "",
                distance_remaining_m = Round(ap.DistanceRemaining),
                path_obstacle_m = float.IsInfinity(ap.PathObstacleDistance) ? -1 : Round(ap.PathObstacleDistance),
                front_clearance_m = Clearance("front"),
                left_clearance_m = Clearance("left"),
                right_clearance_m = Clearance("right"),
                rear_clearance_m = Clearance("rear"),
                lane_shift_m = Round(ap.LaneShift),
                next_signal = ap.NextSignal ?? "",
                red_light_stops = ap.RedLightStops,
                red_light_violations = ap.RedLightViolations,
                time_at_lights_s = Round(ap.TimeAtLights),
                is_blocked = ap.CurrentState == Autopilot.State.Blocked,
                blocked = ap.CurrentState == Autopilot.State.Blocked ? ap.Blocked : new BlockInfo(),
                collisions = car.Collisions,
                sensors = sensors == null
                    ? Array.Empty<SensorState>()
                    : sensors.Readings.Select(r => new SensorState
                    {
                        name = r.Name,
                        angle = r.Angle,
                        range = r.Range,
                        distance = Round(r.Distance),
                        hit = r.Hit ? Scenario.Obstacle.Describe(r.Collider) : "",
                    }).ToArray(),
            };
        }

        public static double Round(float v) => Math.Round(v, 1);
    }
}
