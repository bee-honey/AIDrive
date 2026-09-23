using System;
using System.Linq;
using AIDrive.Navigation;
using UnityEditor;
using UnityEngine;

namespace AIDrive.Editor
{
    [CustomEditor(typeof(RouteDebugger))]
    public class RouteDebuggerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var dbg = (RouteDebugger)target;
            var map = CityMap.Instance;
            if (map == null)
            {
                EditorGUILayout.HelpBox("No CityMap in the scene. Run AIDrive → Generate City.", MessageType.Warning);
                return;
            }

            string[] names;
            try { names = map.Graph.Landmarks.Select(n => n.Name).OrderBy(n => n).ToArray(); }
            catch (Exception e)
            {
                EditorGUILayout.HelpBox("Road graph failed to build: " + e.Message, MessageType.Error);
                return;
            }

            EditorGUI.BeginChangeCheck();
            int fromIdx = EditorGUILayout.Popup("From", Mathf.Max(0, Array.IndexOf(names, dbg.from)), names);
            int toIdx = EditorGUILayout.Popup("To", Mathf.Max(0, Array.IndexOf(names, dbg.to)), names);
            string avoid = EditorGUILayout.TextField(new GUIContent("Avoid streets", "Comma-separated, e.g. E2, S3"), dbg.avoidStreets);
            var color = EditorGUILayout.ColorField("Route color", dbg.routeColor);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(dbg, "Edit Route Debugger");
                dbg.from = names[fromIdx];
                dbg.to = names[toIdx];
                dbg.avoidStreets = avoid;
                dbg.routeColor = color;
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Plan Route", GUILayout.Height(28)))
            {
                dbg.PlanRoute();
                SceneView.RepaintAll();
            }

            var r = dbg.lastRoute;
            if (r == null) return;
            if (!r.Success)
            {
                EditorGUILayout.HelpBox(r.Error, MessageType.Error);
                return;
            }
            EditorGUILayout.HelpBox(
                r.Summary + $"   ·   {r.NodesExpanded} states expanded\n\n" +
                string.Join("\n", r.Directions.Select((d, i) => $"{i + 1}. {d}")),
                MessageType.Info);
        }
    }
}
