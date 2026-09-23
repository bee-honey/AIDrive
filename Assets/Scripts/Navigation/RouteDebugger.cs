using System;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AIDrive.Navigation
{
    /// <summary>Plans a route between two landmarks from the Inspector and draws it in the Scene view.</summary>
    public class RouteDebugger : MonoBehaviour
    {
        public string from = "Home";
        public string to = "Park B";
        [Tooltip("Comma-separated street names, e.g. \"E2, S3\"")]
        public string avoidStreets = "";
        public Color routeColor = new Color(1f, 0.1f, 0.8f);

        [NonSerialized] public Route lastRoute;

        public Route PlanRoute()
        {
            var map = CityMap.Instance;
            if (map == null)
            {
                Debug.LogWarning("RouteDebugger: no CityMap in scene");
                return null;
            }

            var avoid = avoidStreets.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            lastRoute = RoutePlanner.Plan(map.Graph, from, to, new RouteOptions { AvoidStreets = avoid });

            if (lastRoute.Success)
                Debug.Log($"Route {from} → {to}: {lastRoute.Summary}\n  " + string.Join("\n  ", lastRoute.Directions));
            else
                Debug.LogWarning($"Route {from} → {to} failed: {lastRoute.Error}");
            return lastRoute;
        }

        void OnDrawGizmos()
        {
            var map = CityMap.Instance;
            if (lastRoute == null || !lastRoute.Success || map == null) return;
            var g = map.Graph;
            var pts = lastRoute.NodeIds.Select(id => g.Nodes[id].Position + Vector3.up * 1f).ToArray();
            if (pts.Length == 0) return;

#if UNITY_EDITOR
            Handles.color = routeColor;
            Handles.DrawAAPolyLine(8f, pts);
#endif
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(pts[0], 3f);
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(pts[pts.Length - 1], 3f);
        }
    }
}
