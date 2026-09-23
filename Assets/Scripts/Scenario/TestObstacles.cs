using AIDrive.Navigation;
using AIDrive.Vehicle;
using UnityEngine;

namespace AIDrive.Scenario
{
    public enum ObstacleKind { Cone, Blocker }

    /// <summary>Marks a scene object as a road obstacle. M4 replaces the placeholder shapes with real props.</summary>
    public class Obstacle : MonoBehaviour
    {
        public ObstacleKind kind;
    }

    /// <summary>Places placeholder obstacles on a car's planned path, for testing sensors and avoidance.</summary>
    public static class TestObstacles
    {
        const string RootName = "TestObstacles";
        static Material coneMat, blockerMat;

        /// <summary>Drops an obstacle <paramref name="ahead"/> metres along the car's current path.</summary>
        public static GameObject DropAhead(Autopilot autopilot, ObstacleKind kind, float ahead = 25f)
        {
            if (autopilot.Path == null || !autopilot.IsActive) return null;
            float s = autopilot.Path.Distances[autopilot.Path.ClosestIndex(autopilot.transform.position, 0, autopilot.Path.Points.Count)];
            return PlaceOnPath(autopilot.Path, s + ahead, kind);
        }

        /// <summary>
        /// Cone: 0.8 m box centred in the car's lane. Blocker: 5.8 m wall across both same-direction lanes,
        /// from just right of the centre line to the curb.
        /// </summary>
        public static GameObject PlaceOnPath(LanePath path, float s, ObstacleKind kind, float lateral = 0f)
        {
            var p = path.PointAtDistance(s);
            var tangent = (path.PointAtDistance(s + 1f) - path.PointAtDistance(s - 1f)).normalized;
            var right = LanePath.RightOf(tangent);

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(Root(), true);
            go.transform.rotation = Quaternion.LookRotation(tangent);
            var r = go.GetComponent<MeshRenderer>();
            if (kind == ObstacleKind.Cone)
            {
                go.name = "TestCone";
                go.transform.position = p + right * lateral + Vector3.up * 0.5f;
                go.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
                if (coneMat == null) coneMat = MakeMat(new Color(1f, 0.45f, 0.05f)); // Unity null: survives play-mode exits
                r.sharedMaterial = coneMat;
            }
            else
            {
                const float laneCentre = 1.8f, fromCentreLine = 0.2f, curb = 6f;
                float centre = (fromCentreLine + curb) / 2f - laneCentre + lateral;
                go.name = "TestBlocker";
                go.transform.position = p + right * centre + Vector3.up * 0.55f;
                go.transform.localScale = new Vector3(curb - fromCentreLine, 1.1f, 0.6f);
                if (blockerMat == null) blockerMat = MakeMat(new Color(0.95f, 0.85f, 0.1f));
                r.sharedMaterial = blockerMat;
            }
            go.AddComponent<Obstacle>().kind = kind;
            return go;
        }

        public static void ClearAll()
        {
            var root = GameObject.Find(RootName);
            if (root != null) Object.Destroy(root);
        }

        static Transform Root()
        {
            var root = GameObject.Find(RootName);
            return (root != null ? root : new GameObject(RootName)).transform;
        }

        static Material MakeMat(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            return m;
        }
    }
}
