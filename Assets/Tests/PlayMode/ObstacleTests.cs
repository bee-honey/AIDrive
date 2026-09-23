using System.Collections;
using AIDrive.Scenario;
using AIDrive.Vehicle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AIDrive.Tests
{
    /// <summary>Sensors + avoidance: Home → Hospital drives 234 m straight east on E2 first, so obstacles go there.</summary>
    public class ObstacleTests : DriveTestBase
    {
        /// <summary>Mid-block between S3 and S4 on E2, well away from intersections.</summary>
        const float ObstacleAt = 104f;

        [UnityTest]
        public IEnumerator ConeInLane_CarChangesLaneAndArrives()
        {
            yield return ParkAt("Home");
            Assert.IsTrue(Autopilot.DriveTo("Hospital"));
            TestObstacles.PlaceOnPath(Autopilot.Path, ObstacleAt, ObstacleKind.Cone);

            yield return WaitWhileActive(180f);
            LogTrip("Cone on E2");

            Assert.AreEqual(Autopilot.State.Arrived, Autopilot.CurrentState);
            Assert.AreEqual(0, Autopilot.Vehicle.Collisions, $"hit {Autopilot.Vehicle.LastCollision}");
            Assert.GreaterOrEqual(Autopilot.LaneChanges, 1, "never changed lanes around the cone");
        }

        [UnityTest]
        public IEnumerator RoadClosed_CarStopsAndReportsBlocked()
        {
            yield return ParkAt("Home");
            Assert.IsTrue(Autopilot.DriveTo("Hospital"));
            var blocker = TestObstacles.PlaceOnPath(Autopilot.Path, ObstacleAt, ObstacleKind.RoadClosed);

            yield return WaitFor(Autopilot.State.Blocked, 60f);
            Assert.AreEqual(Autopilot.State.Blocked, Autopilot.CurrentState);
            Assert.AreEqual(0, Autopilot.Vehicle.Collisions, $"hit {Autopilot.Vehicle.LastCollision}");
            Assert.AreEqual(0, Autopilot.LaneChanges, "tried to change into a lane that is also blocked");

            var info = Autopilot.Blocked;
            Debug.Log($"Blocked: {JsonUtility.ToJson(info)}");
            Assert.AreEqual("E2", info.street);
            Assert.AreEqual("S3 & S4", info.between);
            Assert.AreEqual("RoadClosed", info.by);
            Assert.That(info.distance_m, Is.InRange(2.3f, 4f), "stopping gap (target 3 m)");

            // Stays put rather than creeping forward.
            var stoppedAt = Autopilot.transform.position;
            yield return new WaitForSeconds(5f);
            Assert.AreEqual(Autopilot.State.Blocked, Autopilot.CurrentState);
            Assert.Less(Vector3.Distance(stoppedAt, Autopilot.transform.position), 0.1f);

            var state = Autopilot.CaptureState();
            Assert.IsTrue(state.is_blocked);
            StringAssert.Contains("\"street\": \"E2\"", state.ToJson());
            Object.Destroy(blocker);
        }

        [UnityTest]
        public IEnumerator RoadClosedRemoved_CarResumesAndArrives()
        {
            yield return ParkAt("Home");
            Assert.IsTrue(Autopilot.DriveTo("Hospital"));
            var blocker = TestObstacles.PlaceOnPath(Autopilot.Path, ObstacleAt, ObstacleKind.RoadClosed);

            yield return WaitFor(Autopilot.State.Blocked, 60f);
            Assert.AreEqual(Autopilot.State.Blocked, Autopilot.CurrentState);

            Object.Destroy(blocker);
            yield return WaitWhileActive(180f);
            LogTrip("Blocker removed");

            Assert.AreEqual(Autopilot.State.Arrived, Autopilot.CurrentState);
            Assert.AreEqual(0, Autopilot.Vehicle.Collisions);
        }
    }
}
