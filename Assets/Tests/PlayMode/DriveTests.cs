using System.Collections;
using AIDrive.Navigation;
using AIDrive.Vehicle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace AIDrive.Tests
{
    /// <summary>End-to-end: the car drives between landmarks in the real scene.</summary>
    public class DriveTests
    {
        const float TimeScale = 4f;
        const float TimeoutSimSeconds = 180f;

        [UnitySetUp]
        public IEnumerator LoadCity()
        {
            SceneManager.LoadScene("SampleScene");
            yield return null;
            yield return null;
            Time.timeScale = TimeScale;
        }

        [TearDown]
        public void RestoreTime() => Time.timeScale = 1f;

        [UnityTest] public IEnumerator Home_To_ParkB() => Drive("Home", "Park B");
        [UnityTest] public IEnumerator Home_To_Hospital() => Drive("Home", "Hospital");
        [UnityTest] public IEnumerator School_To_GasStation() => Drive("School", "Gas Station");

        IEnumerator Drive(string from, string to)
        {
            var autopilot = Object.FindAnyObjectByType<Autopilot>();
            Assert.IsNotNull(autopilot, "no car in scene");
            // Clicks in the Game view must not redirect the car mid-test.
            var hud = autopilot.GetComponent<AIDrive.UI.DriveHud>();
            if (hud != null) hud.enabled = false;
            var graph = CityMap.Instance.Graph;
            graph.TryGetLandmark(from, out var start);
            graph.TryGetLandmark(to, out var goal);

            var pose = LanePath.CurbPose(start, LaneSettings.Default);
            autopilot.Vehicle.Teleport(pose.position, pose.rotation);
            autopilot.Vehicle.ResetStats();
            yield return new WaitForFixedUpdate();

            Assert.IsTrue(autopilot.DriveTo(to), autopilot.LastError);
            float started = Time.time;
            while (autopilot.CurrentState == Autopilot.State.Driving && Time.time - started < TimeoutSimSeconds)
                yield return null;

            var end = LanePath.CurbPose(goal, LaneSettings.Default).position;
            var pos = autopilot.transform.position;
            Debug.Log($"{from} → {to}: {autopilot.CurrentState} in {autopilot.TripTime:0.0}s, " +
                      $"{autopilot.Vehicle.DistanceTravelled:0} m driven, route {autopilot.Route.Length:0} m, " +
                      $"max lane error {autopilot.MaxCrossTrackError:0.00} m at {autopilot.MaxCrossTrackAt:0}/{autopilot.Path.Length:0} m " +
                      $"(maneuvers: {string.Join(", ", System.Linq.Enumerable.Select(autopilot.Path.Maneuvers, m => $"{m.Text}@{m.Distance:0}"))}), collisions {autopilot.Vehicle.Collisions}");

            Assert.AreEqual(Autopilot.State.Arrived, autopilot.CurrentState, $"did not arrive within {TimeoutSimSeconds}s");
            Assert.AreEqual(0, autopilot.Vehicle.Collisions, $"hit {autopilot.Vehicle.LastCollision}");
            Assert.Less(Vector3.Distance(new Vector3(pos.x, 0, pos.z), end), 5f, "parked outside the drop-off zone");
            Assert.Less(autopilot.MaxCrossTrackError, 1.0f, "drifted out of lane");
        }
    }
}
