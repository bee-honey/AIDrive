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
        System.Collections.Generic.List<ScenarioDef> scenarios;
        ScenarioRunner runner;
        int scenarioIndex;
        GUIStyle box, label, mono;
        bool showJson;
        Vector2 jsonScroll;

        void Start()
        {
            landmarks = CityMap.Instance.Graph.Landmarks.Select(n => n.Name).OrderBy(n => n).ToArray();
            scenarios = ScenarioLibrary.LoadAll();
            runner = autopilot != null ? autopilot.GetComponent<ScenarioRunner>() : null;
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
            if (autopilot.NextSignal != null)
                GUILayout.Label($"<b>Signal:</b> {SignalText(autopilot.NextSignal)}", label);
            GUILayout.Label($"<b>Lights:</b> {autopilot.RedLightStops} stop(s), {autopilot.TimeAtLights:0} s waiting" +
                            (autopilot.RedLightViolations > 0 ? $" · <color=#ff6666>{autopilot.RedLightViolations} violation(s)</color>" : ""), label);
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
            if (GUILayout.Button("Drop road-closed ahead", GUILayout.Height(24))) TestObstacles.DropAhead(autopilot, ObstacleKind.RoadClosed);
            GUI.enabled = true;
            if (GUILayout.Button("Clear", GUILayout.Height(24), GUILayout.Width(50))) TestObstacles.ClearAll();
            GUILayout.EndHorizontal();

            if (runner != null && scenarios.Count > 0)
            {
                GUILayout.Space(6);
                var sc = scenarios[scenarioIndex];
                GUILayout.Label($"<b>Scenario:</b> {sc.name} <i>({sc.start} → {sc.destination}, expect {sc.Expect})</i>", label);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("◀", GUILayout.Width(30), GUILayout.Height(24))) scenarioIndex = (scenarioIndex + scenarios.Count - 1) % scenarios.Count;
                if (GUILayout.Button("▶", GUILayout.Width(30), GUILayout.Height(24))) scenarioIndex = (scenarioIndex + 1) % scenarios.Count;
                if (GUILayout.Button(runner.Running ? "Running…" : "Run scenario", GUILayout.Height(24))) runner.StartScenario(sc);
                GUILayout.EndHorizontal();
                if (!string.IsNullOrEmpty(sc.description)) GUILayout.Label($"<size=11>{sc.description}</size>", label);
                var res = runner.LastResult;
                if (res != null)
                    GUILayout.Label(res.passed
                        ? $"<color=#66ff88><b>PASSED</b></color> {res.scenario}: {res.outcome} in {res.time_s}s, {res.lane_changes} lane change(s)"
                        : $"<color=#ff6666><b>FAILED</b></color> {res.scenario}: {res.failure}", label);
            }

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

        static string SignalText(string s) =>
            s.StartsWith("Red") ? $"<color=#ff6666>{s}</color>" :
            s.StartsWith("Yellow") ? $"<color=#ffcc44>{s}</color>" : $"<color=#66ff88>{s}</color>";

        string StateText()
        {
            switch (autopilot.CurrentState)
            {
                case Autopilot.State.StoppedAtLight: return "<color=#ff9944>Stopped at red light</color>";
                case Autopilot.State.Waiting: return "<color=#ffcc44>Waiting (obstacle)</color>";
                case Autopilot.State.Blocked: return "<color=#ff6666>Blocked</color>";
                case Autopilot.State.Arrived: return "<color=#66ff88>Arrived</color>";
                default: return autopilot.CurrentState.ToString();
            }
        }
    }
}
