using AIDrive.Navigation;
using AIDrive.Vehicle;
using UnityEngine;

namespace AIDrive.Scenario
{
    /// <summary>Drops props relative to a car's planned path, for quick manual and automated tests.</summary>
    public static class TestObstacles
    {
        /// <summary>Drops a prop <paramref name="ahead"/> metres along the car's current path.</summary>
        public static GameObject DropAhead(Autopilot autopilot, ObstacleKind kind, float ahead = 25f)
        {
            if (autopilot.Path == null || !autopilot.IsActive) return null;
            float s = autopilot.Path.Distances[autopilot.Path.ClosestIndex(autopilot.transform.position, 0, autopilot.Path.Points.Count)];
            return PlaceOnPath(autopilot.Path, s + ahead, kind);
        }

        /// <summary>
        /// Places a prop at distance <paramref name="s"/> along a path, in the lane that prop type normally occupies
        /// (cone/barrier: the car's lane; road-closed: both same-direction lanes; parked car: curb lane).
        /// </summary>
        public static GameObject PlaceOnPath(LanePath path, float s, ObstacleKind kind, float extraLateral = 0f)
        {
            var p = path.PointAtDistance(s);
            var tangent = (path.PointAtDistance(s + 1f) - path.PointAtDistance(s - 1f)).normalized;
            float pathLane = LaneSettings.Default.LaneOffset; // the path runs this far right of the centre line
            float lateral;
            switch (kind)
            {
                case ObstacleKind.RoadClosed: lateral = RoadAddress.BothLanes - pathLane; break;
                case ObstacleKind.ParkedCar: lateral = RoadAddress.CurbLane - pathLane; break;
                default: lateral = 0f; break;
            }
            return PropPlacer.Spawn(kind, p + LanePath.RightOf(tangent) * (lateral + extraLateral), tangent);
        }

        public static void ClearAll() => PropPlacer.ClearAll();
    }
}
