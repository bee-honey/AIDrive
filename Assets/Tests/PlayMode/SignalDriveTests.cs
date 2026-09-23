using System.Collections;
using System.Linq;
using AIDrive.Traffic;
using AIDrive.Vehicle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AIDrive.Tests
{
    /// <summary>Home → Hospital heads east on E2, crossing signals at S2 & E2 and S4 & E2 (S3 & E2 has none).</summary>
    public class SignalDriveTests : DriveTestBase
    {
        [UnityTest]
        public IEnumerator ForcedRed_CarStopsAtLine_ThenGoesOnGreen()
        {
            var s2 = TrafficSignal.At(1, 1);
            var s4 = TrafficSignal.At(3, 1);
            Assert.IsNotNull(s4, "no signal at S4 & E2");
            Assert.IsNull(TrafficSignal.At(2, 1), "S3 & E2 should be unsignalised");
            s2.Force(SignalAxis.EastWest, SignalState.Green); // let the car through the first one
            s4.Force(SignalAxis.EastWest, SignalState.Red);

            yield return ParkAt("Home");
            Assert.IsTrue(Autopilot.DriveTo("Hospital"));
            yield return WaitFor(Autopilot.State.StoppedAtLight, 60f);
            Assert.AreEqual(Autopilot.State.StoppedAtLight, Autopilot.CurrentState);
            StringAssert.Contains("S4 & E2", Autopilot.NextSignal);

            // Front bumper just behind the stop line, not over it.
            var stop = Autopilot.Path.StopPoints.First(p => p.NodeId == Graph.Intersection(3, 1).Id);
            var front = Autopilot.transform.position + Autopilot.transform.forward * 2.1f;
            float gap = Vector3.Dot(Autopilot.Path.PointAtDistance(stop.Distance) - new Vector3(front.x, 0, front.z), stop.Direction);
            Debug.Log($"Stopped {gap:0.00} m before the S4 & E2 stop line");
            Assert.That(gap, Is.InRange(0f, 1.5f));

            // Holds while red.
            yield return new WaitForSeconds(4f);
            Assert.AreEqual(Autopilot.State.StoppedAtLight, Autopilot.CurrentState);

            s4.Force(SignalAxis.EastWest, SignalState.Green);
            yield return WaitWhileActive(180f);
            LogTrip("Forced red at S4 & E2");
            Assert.AreEqual(Autopilot.State.Arrived, Autopilot.CurrentState);
            Assert.AreEqual(0, Autopilot.RedLightViolations);
            Assert.GreaterOrEqual(Autopilot.RedLightStops, 1);
            Assert.AreEqual(0, Autopilot.Vehicle.Collisions);
        }

        [UnityTest]
        public IEnumerator LiveSignals_NoViolationsOnLongDrive()
        {
            yield return ParkAt("School");
            Assert.IsTrue(Autopilot.DriveTo("Gas Station"));
            yield return WaitWhileActive(240f);
            LogTrip("School → Gas Station with live signals");
            Assert.AreEqual(Autopilot.State.Arrived, Autopilot.CurrentState);
            Assert.AreEqual(0, Autopilot.RedLightViolations);
            Assert.AreEqual(0, Autopilot.Vehicle.Collisions);
        }
    }
}
