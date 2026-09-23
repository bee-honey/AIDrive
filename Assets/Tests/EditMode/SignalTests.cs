using System.Linq;
using AIDrive.Navigation;
using AIDrive.Traffic;
using NUnit.Framework;
using UnityEngine;

namespace AIDrive.Tests
{
    public class SignalTests
    {
        [Test]
        public void Timing_FollowsTwoPhasePlan()
        {
            Assert.AreEqual(SignalState.Green, SignalTiming.StateAt(0f, 0f, SignalAxis.NorthSouth));
            Assert.AreEqual(SignalState.Red, SignalTiming.StateAt(0f, 0f, SignalAxis.EastWest));
            Assert.AreEqual(SignalState.Yellow, SignalTiming.StateAt(SignalTiming.Green + 0.5f, 0f, SignalAxis.NorthSouth));
            Assert.AreEqual(SignalState.Red, SignalTiming.StateAt(SignalTiming.Green + SignalTiming.Yellow + 0.5f, 0f, SignalAxis.NorthSouth));
            Assert.AreEqual(SignalState.Green, SignalTiming.StateAt(SignalTiming.Half + 0.5f, 0f, SignalAxis.EastWest));
            Assert.AreEqual(SignalState.Green, SignalTiming.StateAt(SignalTiming.Cycle + 1f, 0f, SignalAxis.NorthSouth), "cycle repeats");
        }

        [Test]
        public void Timing_NeverGreenForBothAxes_AndHasAllRedClearance()
        {
            int allRedSamples = 0;
            for (float t = 0f; t < SignalTiming.Cycle; t += 0.1f)
            {
                var ns = SignalTiming.StateAt(t, 7f, SignalAxis.NorthSouth);
                var ew = SignalTiming.StateAt(t, 7f, SignalAxis.EastWest);
                Assert.IsFalse(ns != SignalState.Red && ew != SignalState.Red, $"conflicting phases at t={t}");
                if (ns == SignalState.Red && ew == SignalState.Red) allRedSamples++;
            }
            Assert.Greater(allRedSamples, 20, "expected ~3 s of all-red per cycle");
        }

        [Test]
        public void Axis_MatchesTravelDirection()
        {
            Assert.AreEqual(SignalAxis.NorthSouth, SignalTiming.AxisOf(Vector3.forward));
            Assert.AreEqual(SignalAxis.NorthSouth, SignalTiming.AxisOf(Vector3.back));
            Assert.AreEqual(SignalAxis.EastWest, SignalTiming.AxisOf(Vector3.right));
        }

        [Test]
        public void Signals_OnlyWhereMainRoadsCross()
        {
            int count = 0;
            for (int i = 0; i < AIDrive.City.CityLayout.RoadCount; i++)
            for (int j = 0; j < AIDrive.City.CityLayout.RoadCount; j++)
                if (AIDrive.City.CityLayout.IsSignalized(i, j)) count++;
            Assert.AreEqual(9, count);
            Assert.IsTrue(AIDrive.City.CityLayout.IsSignalized(1, 1), "S2 & E2");
            Assert.IsFalse(AIDrive.City.CityLayout.IsSignalized(2, 1), "S3 & E2");
            Assert.IsFalse(AIDrive.City.CityLayout.IsSignalized(0, 1), "edge");
        }

        [Test]
        public void Path_HasStopPointBeforeEveryIntersection()
        {
            var graph = RoadGraph.BuildDefault();
            graph.TryGetLandmark("Home", out var home);
            var lane = LaneSettings.Default;
            var pose = LanePath.CurbPose(home, lane);
            var route = RoutePlanner.PlanFrom(graph, pose.position, pose.forward, "Hospital", null, out var loc);
            var dir = (graph.Nodes[loc.NextNode].Position - graph.Nodes[loc.FromNode].Position).normalized;
            var path = LanePath.Build(graph, route, pose.position, dir, lane);

            int intersections = route.NodeIds.Count(id => graph.Nodes[id].Kind == NodeKind.Intersection);
            Assert.AreEqual(intersections, path.StopPoints.Count);

            float last = -1f;
            foreach (var sp in path.StopPoints)
            {
                Assert.Greater(sp.Distance, last, "stop points are ordered along the path");
                last = sp.Distance;
                var centre = graph.Nodes[sp.NodeId].Position;
                float before = Vector3.Dot(centre - path.PointAtDistance(sp.Distance), sp.Direction);
                Assert.AreEqual(lane.StopLine, before, 0.3f, $"stop line for {graph.Nodes[sp.NodeId].Name}");
            }
        }
    }
}
