using System.Collections;
using AIDrive.Navigation;
using AIDrive.Vehicle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AIDrive.Tests
{
    /// <summary>End-to-end: the car drives between landmarks in the real scene.</summary>
    public class DriveTests : DriveTestBase
    {
        const float TimeoutSimSeconds = 180f;

        [UnityTest] public IEnumerator Home_To_ParkB() => Drive("Home", "Park B");
        [UnityTest] public IEnumerator Home_To_Hospital() => Drive("Home", "Hospital");
        [UnityTest] public IEnumerator School_To_GasStation() => Drive("School", "Gas Station");

        IEnumerator Drive(string from, string to)
        {
            yield return ParkAt(from);
            Assert.IsTrue(Autopilot.DriveTo(to), Autopilot.LastError);
            yield return WaitWhileActive(TimeoutSimSeconds);
            LogTrip($"{from} → {to}");

            Graph.TryGetLandmark(to, out var goal);
            var end = LanePath.CurbPose(goal, LaneSettings.Default).position;
            var pos = Autopilot.transform.position;
            Assert.AreEqual(Autopilot.State.Arrived, Autopilot.CurrentState, $"did not arrive within {TimeoutSimSeconds}s");
            Assert.AreEqual(0, Autopilot.Vehicle.Collisions, $"hit {Autopilot.Vehicle.LastCollision}");
            Assert.Less(Vector3.Distance(new Vector3(pos.x, 0, pos.z), end), 5f, "parked outside the drop-off zone");
            Assert.Less(Autopilot.MaxCrossTrackError, 1.0f, "drifted out of lane");
            Assert.AreEqual(0, Autopilot.LaneChanges, "changed lanes on an empty road (false obstacle?)");
        }
    }
}
