using System.Collections;
using AIDrive.Navigation;
using AIDrive.UI;
using AIDrive.Vehicle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace AIDrive.Tests
{
    /// <summary>Loads the city, speeds up time, and parks the car at a landmark ready to drive.</summary>
    public abstract class DriveTestBase
    {
        protected const float TimeScale = 4f;
        protected Autopilot Autopilot;
        protected RoadGraph Graph;

        [UnitySetUp]
        public IEnumerator LoadCity()
        {
            SceneManager.LoadScene("SampleScene");
            yield return null;
            yield return null;
            Time.timeScale = TimeScale;

            Autopilot = Object.FindAnyObjectByType<Autopilot>();
            Assert.IsNotNull(Autopilot, "no car in scene");
            Graph = CityMap.Instance.Graph;
            // Clicks in the Game view must not redirect the car mid-test.
            var hud = Autopilot.GetComponent<DriveHud>();
            if (hud != null) hud.enabled = false;
        }

        [TearDown]
        public void RestoreTime() => Time.timeScale = 1f;

        protected IEnumerator ParkAt(string landmark)
        {
            Graph.TryGetLandmark(landmark, out var node);
            var pose = LanePath.CurbPose(node, LaneSettings.Default);
            Autopilot.Vehicle.Teleport(pose.position, pose.rotation);
            Autopilot.Vehicle.ResetStats();
            yield return new WaitForFixedUpdate();
        }

        /// <summary>Yields until the autopilot reaches <paramref name="state"/> or <paramref name="timeout"/> sim-seconds pass.</summary>
        protected IEnumerator WaitFor(Autopilot.State state, float timeout)
        {
            float started = Time.time;
            while (Autopilot.CurrentState != state && Time.time - started < timeout)
                yield return null;
        }

        protected IEnumerator WaitWhileActive(float timeout)
        {
            float started = Time.time;
            while (Autopilot.IsActive && Time.time - started < timeout)
                yield return null;
        }

        protected void LogTrip(string label)
        {
            Debug.Log($"{label}: {Autopilot.CurrentState} in {Autopilot.TripTime:0.0}s, " +
                      $"{Autopilot.Vehicle.DistanceTravelled:0} m driven, route {Autopilot.Route?.Length:0} m, " +
                      $"max lane error {Autopilot.MaxCrossTrackError:0.00} m at {Autopilot.MaxCrossTrackAt:0}/{Autopilot.Path?.Length:0} m, " +
                      $"lane changes {Autopilot.LaneChanges}, collisions {Autopilot.Vehicle.Collisions}, " +
                      $"red-light stops {Autopilot.RedLightStops} ({Autopilot.TimeAtLights:0} s), violations {Autopilot.RedLightViolations}");
        }
    }
}
