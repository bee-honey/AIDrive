using System.Linq;
using AIDrive.Navigation;
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
        GUIStyle box, label;

        void Start()
        {
            landmarks = CityMap.Instance.Graph.Landmarks.Select(n => n.Name).OrderBy(n => n).ToArray();
        }

        void OnGUI()
        {
            if (autopilot == null) return;
            box ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, padding = new RectOffset(10, 10, 8, 8) };
            label ??= new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };

            var car = autopilot.Vehicle;
            GUILayout.BeginArea(new Rect(10, 10, 330, Screen.height - 20));
            GUILayout.BeginVertical(box);

            GUILayout.Label($"<b>State:</b> {autopilot.CurrentState}" +
                            (autopilot.Destination != null ? $" → {autopilot.Destination}" : ""), Rich());
            GUILayout.Label($"<b>Speed:</b> {car.Speed * 3.6f:0} km/h", Rich());
            GUILayout.Label($"<b>Location:</b> {autopilot.Locate()}", Rich());
            if (autopilot.Route != null)
                GUILayout.Label($"<b>Route:</b> {autopilot.Route.Summary}", Rich());
            var next = autopilot.NextInstruction;
            if (next != null) GUILayout.Label($"<b>Next:</b> {next}", Rich());
            if (autopilot.CurrentState == Autopilot.State.Driving)
                GUILayout.Label($"<b>Remaining:</b> {autopilot.DistanceRemaining:0} m", Rich());
            GUILayout.Label($"<b>Lane error:</b> {autopilot.CrossTrackError:0.00} m (max {autopilot.MaxCrossTrackError:0.00})", Rich());
            GUILayout.Label($"<b>Collisions:</b> {car.Collisions}" + (car.LastCollision != null ? $" (last: {car.LastCollision})" : ""), Rich());
            if (autopilot.LastError != null)
                GUILayout.Label($"<color=#ff6666><b>Error:</b> {autopilot.LastError}</color>", Rich());

            GUILayout.Space(6);
            GUILayout.Label("<b>Drive to:</b>", Rich());
            for (int i = 0; i < landmarks.Length; i += 2)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(i + 2, landmarks.Length); j++)
                    if (GUILayout.Button(landmarks[j], GUILayout.Height(24))) autopilot.DriveTo(landmarks[j]);
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Stop", GUILayout.Height(24))) autopilot.Stop();
            if (cameraRig != null && GUILayout.Button($"Camera: {cameraRig.mode}", GUILayout.Height(24))) cameraRig.Toggle();
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        GUIStyle Rich()
        {
            label.richText = true;
            return label;
        }
    }
}
