using System.Linq;
using AIDrive.Navigation;
using AIDrive.Scenario;
using AIDrive.Vehicle;
using UnityEngine;

namespace AIDrive.UI
{
    /// <summary>Minimal IMGUI panel for testing the autopilot. The full UI arrives in M7.</summary>
    public class DriveHud : MonoBehaviour
    {
        public Autopilot autopilot;
        public CameraRig cameraRig;

        string[] landmarks;
        GUIStyle box, label, mono;
        bool showJson;
        Vector2 jsonScroll;

        void Start()
        {
            landmarks = CityMap.Instance.Graph.Landmarks.Select(n => n.Name).OrderBy(n => n).ToArray();
        }

        void OnGUI()
        {
            if (autopilot == null) return;
            box ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, padding = new RectOffset(10, 10, 8, 8) };
            label ??= new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true, richText = true };
            mono ??= new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = false, font = Font.CreateDynamicFontFromOSFont("Menlo", 11) };

            var car = autopilot.Vehicle;
            GUILayout.BeginArea(new Rect(10, 10, 340, Screen.height - 20));
            GUILayout.BeginVertical(box);

            GUILayout.Label($"<b>State:</b> {StateText()}" +
                            (autopilot.Destination != null ? $" → {autopilot.Destination}" : ""), label);
            GUILayout.Label($"<b>Speed:</b> {car.Speed * 3.6f:0} km/h", label);
            GUILayout.Label($"<b>Location:</b> {autopilot.Locate()}", label);
            if (autopilot.Route != null)
                GUILayout.Label($"<b>Route:</b> {autopilot.Route.Summary}", label);
            var next = autopilot.NextInstruction;
            if (next != null) GUILayout.Label($"<b>Next:</b> {next}", label);
            if (autopilot.IsActive)
                GUILayout.Label($"<b>Remaining:</b> {autopilot.DistanceRemaining:0} m", label);
            if (!float.IsInfinity(autopilot.PathObstacleDistance))
                GUILayout.Label($"<b>Obstacle ahead:</b> {autopilot.PathObstacleName} in {autopilot.PathObstacleDistance:0.0} m", label);
            if (autopilot.CurrentState == Autopilot.State.Blocked && autopilot.Blocked != null)
                GUILayout.Label($"<color=#ff6666><b>Blocked:</b> {autopilot.Blocked.street} between {autopilot.Blocked.between} by {autopilot.Blocked.by}</color>", label);
            GUILayout.Label($"<b>Lane error:</b> {autopilot.CrossTrackError:0.00} m (max {autopilot.MaxCrossTrackError:0.00}) · lane changes {autopilot.LaneChanges}", label);
            GUILayout.Label($"<b>Collisions:</b> {car.Collisions}" + (car.LastCollision != null ? $" (last: {car.LastCollision})" : ""), label);
            if (autopilot.LastError != null)
                GUILayout.Label($"<color=#ff6666><b>Error:</b> {autopilot.LastError}</color>", label);

            GUILayout.Space(6);
            GUILayout.Label("<b>Drive to:</b>", label);
            for (int i = 0; i < landmarks.Length; i += 2)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(i + 2, landmarks.Length); j++)
                    if (GUILayout.Button(landmarks[j], GUILayout.Height(24))) autopilot.DriveTo(landmarks[j]);
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(6);
            GUILayout.Label("<b>Test obstacles:</b>", label);
            GUILayout.BeginHorizontal();
            GUI.enabled = autopilot.IsActive;
            if (GUILayout.Button("Drop cone ahead", GUILayout.Height(24))) TestObstacles.DropAhead(autopilot, ObstacleKind.Cone);
            if (GUILayout.Button("Drop blocker ahead", GUILayout.Height(24))) TestObstacles.DropAhead(autopilot, ObstacleKind.Blocker);
            GUI.enabled = true;
            if (GUILayout.Button("Clear", GUILayout.Height(24), GUILayout.Width(50))) TestObstacles.ClearAll();
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Stop", GUILayout.Height(24))) autopilot.Stop();
            if (cameraRig != null && GUILayout.Button($"Camera: {cameraRig.mode}", GUILayout.Height(24))) cameraRig.Toggle();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (autopilot.Sensors != null)
                autopilot.Sensors.showRays = GUILayout.Toggle(autopilot.Sensors.showRays, " Show rays");
            showJson = GUILayout.Toggle(showJson, " Show state JSON");
            GUILayout.EndHorizontal();

            if (showJson)
            {
                jsonScroll = GUILayout.BeginScrollView(jsonScroll, GUILayout.Height(260));
                GUILayout.Label(autopilot.CaptureState().ToJson(), mono);
                GUILayout.EndScrollView();
            }

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        string StateText()
        {
            switch (autopilot.CurrentState)
            {
                case Autopilot.State.Waiting: return "<color=#ffcc44>Waiting (obstacle)</color>";
                case Autopilot.State.Blocked: return "<color=#ff6666>Blocked</color>";
                case Autopilot.State.Arrived: return "<color=#66ff88>Arrived</color>";
                default: return autopilot.CurrentState.ToString();
            }
        }
    }
}
