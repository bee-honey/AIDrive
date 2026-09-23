using UnityEngine;

namespace AIDrive.Scenario
{
    public enum ObstacleKind { Cone, Barrier, RoadClosed, JerseyBarrier, ParkedCar }

    /// <summary>Marks a prop as a road obstacle so sensors and state reports can name what they see.</summary>
    public class Obstacle : MonoBehaviour
    {
        public ObstacleKind kind;

        /// <summary>Obstacle kind for props ("RoadClosed"), otherwise the collider's name ("Building").</summary>
        public static string Describe(Collider c)
        {
            if (c == null) return "";
            var o = c.GetComponentInParent<Obstacle>();
            return o != null ? o.kind.ToString() : c.name;
        }
    }
}
