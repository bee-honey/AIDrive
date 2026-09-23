using System.Linq;
using AIDrive.City;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AIDrive.Navigation
{
    /// <summary>Scene entry point for navigation: owns the road graph built from the scene's landmarks.</summary>
    public class CityMap : MonoBehaviour
    {
        public bool drawGraph = true;
        public bool drawNodeLabels = true;

        RoadGraph graph;
        static CityMap instance;

        public static CityMap Instance
        {
            get
            {
                if (instance == null) instance = FindAnyObjectByType<CityMap>();
                return instance;
            }
        }

        public RoadGraph Graph => graph ?? Rebuild();

        public RoadGraph Rebuild()
        {
            var landmarks = GetComponentsInChildren<Landmark>(true).Select(l => l.ToDef());
            graph = RoadGraph.Build(landmarks);
            return graph;
        }

        void OnValidate() => graph = null;

        void OnDrawGizmos()
        {
            if (!drawGraph) return;
            RoadGraph g;
            try { g = Graph; }
            catch { return; }

            var up = Vector3.up * 0.3f;
            foreach (var e in g.Edges)
            {
                Gizmos.color = e.Blocked ? Color.red : new Color(1f, 0.85f, 0f, 0.8f);
                Gizmos.DrawLine(g.Nodes[e.A].Position + up, g.Nodes[e.B].Position + up);
            }

            foreach (var n in g.Nodes)
            {
                bool lm = n.Kind == NodeKind.Landmark;
                Gizmos.color = lm ? Color.cyan : Color.yellow;
                Gizmos.DrawSphere(n.Position + up, lm ? 1.5f : 0.8f);
#if UNITY_EDITOR
                if (drawNodeLabels) Handles.Label(n.Position + Vector3.up * 3f, n.Name);
#endif
            }
        }
    }
}
