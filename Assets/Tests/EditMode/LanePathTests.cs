using System.Linq;
using AIDrive.City;
using AIDrive.Navigation;
using NUnit.Framework;
using UnityEngine;

namespace AIDrive.Tests
{
    public class LanePathTests
    {
        RoadGraph graph;
        LaneSettings lane;

        [SetUp]
        public void SetUp()
        {
            graph = RoadGraph.BuildDefault();
            lane = LaneSettings.Default;
        }

        LanePath PathFromHome(string to, out Route route)
        {
            graph.TryGetLandmark("Home", out var home);
            var pose = LanePath.CurbPose(home, lane);
            route = RoutePlanner.PlanFrom(graph, pose.position, pose.forward, to, null, out var loc);
            Assert.IsTrue(route.Success, route.Error);
            var dir = (graph.Nodes[loc.NextNode].Position - graph.Nodes[loc.FromNode].Position).normalized;
            return LanePath.Build(graph, route, pose.position, dir, lane);
        }

        [Test]
        public void Path_IsContinuousAndEvenlySpaced()
        {
            var path = PathFromHome("Hospital", out _);
            for (int i = 1; i < path.Points.Count; i++)
                Assert.LessOrEqual(Vector3.Distance(path.Points[i - 1], path.Points[i]), lane.Spacing + 0.01f, $"gap at {i}");
            Assert.AreEqual(path.Points.Count, path.Distances.Count);
        }

        [Test]
        public void Path_StaysInRightLaneBetweenIntersections()
        {
            var path = PathFromHome("Hospital", out var route);
            int checkedPoints = 0;
            for (int i = 1; i < path.Points.Count - 20; i++) // skip the final pull-over
            {
                if (path.Distances[i] < 13f) continue; // merging out of the parking spot
                var dir = (path.Points[i + 1] - path.Points[i - 1]).normalized;
                var loc = Localizer.Locate(graph, path.Points[i], dir);
                if (loc.NearLandmark != null || InTurnZone(path.Points[i])) continue;
                Assert.AreEqual(lane.LaneOffset, loc.LateralOffset, 0.3f, $"point {i} on {loc.Street}");
                checkedPoints++;
            }
            Assert.Greater(checkedPoints, 100);
        }

        /// <summary>Within a turn curve's reach of any intersection centre.</summary>
        bool InTurnZone(Vector3 p) =>
            graph.Nodes.Any(n => n.Kind == NodeKind.Intersection && Vector3.Distance(n.Position, p) < lane.TurnEntry + 0.5f);

        [Test]
        public void Path_EndsParkedInDropOffZone()
        {
            var path = PathFromHome("Park B", out _);
            graph.TryGetLandmark("Park B", out var parkB);
            var end = path.Points.Last();
            var curb = LanePath.CurbPose(parkB, lane);
            Assert.Less(Vector3.Distance(end, curb.position), 5f, "stops inside the 10 m drop-off zone");
            var toBlock = (parkB.BlockCenter - parkB.Position).normalized;
            Assert.AreEqual(lane.CurbOffset, Vector3.Dot(end - parkB.Position, toBlock), 0.3f, "pulled over to the landmark's curb");
        }

        [Test]
        public void Path_HasManeuversForEachTurnPlusArrival()
        {
            var path = PathFromHome("Hospital", out var route);
            Assert.AreEqual(route.Turns + 1, path.Maneuvers.Count);
            StringAssert.StartsWith("Arrive at Hospital", path.Maneuvers.Last().Text);
            for (int i = 1; i < path.Maneuvers.Count; i++)
                Assert.Greater(path.Maneuvers[i].Distance, path.Maneuvers[i - 1].Distance);
        }

        [Test]
        public void Project_SeparatesInLaneHitsFromRoadside()
        {
            var path = PathFromHome("Hospital", out _);
            var p = path.PointAtDistance(100f);
            var t = (path.PointAtDistance(101f) - path.PointAtDistance(99f)).normalized;
            var right = LanePath.RightOf(t);

            // A cone slightly right of lane centre
            Assert.IsTrue(path.Project(p + right * 0.4f + Vector3.up * 0.5f, 60, 80, out float s, out float lat));
            Assert.AreEqual(100f, s, 0.6f);
            Assert.AreEqual(0.4f, lat, 0.05f);

            // A building face across the sidewalk: far outside the 1.45 m corridor
            Assert.IsTrue(path.Project(p + right * 6.5f, 60, 80, out _, out float building));
            Assert.Greater(Mathf.Abs(building), 4f);

            // A point in the oncoming lanes is to the left
            Assert.IsTrue(path.Project(p - right * 3.6f, 60, 80, out _, out float oncoming));
            Assert.Less(oncoming, -3f);
        }

        [Test]
        public void CurbPose_FacesSoLandmarkIsOnTheRight()
        {
            foreach (var lm in graph.Landmarks)
            {
                var pose = LanePath.CurbPose(lm, lane);
                var right = LanePath.RightOf(pose.forward);
                Assert.Greater(Vector3.Dot(right, lm.BlockCenter - lm.Position), 0f, lm.Name);
            }
        }

        [Test]
        public void Planner_PrefersArrivingWithDestinationOnTheRight()
        {
            var names = graph.Landmarks.Select(n => n.Name).ToList();
            foreach (var from in names)
            foreach (var to in names)
            {
                if (from == to) continue;
                var r = RoutePlanner.Plan(graph, from, to);
                StringAssert.EndsWith("on your right", r.Directions.Last(), $"{from} → {to}");
            }
        }

        [Test]
        public void Localizer_DescribesRoadPosition()
        {
            // Right lane of E2, between S3 and S4, heading east.
            float x = (CityLayout.RoadCenter(2) + CityLayout.RoadCenter(3)) / 2f;
            var pos = new Vector3(x, 0, CityLayout.RoadCenter(1) - lane.LaneOffset);
            var loc = Localizer.Locate(graph, pos, Vector3.right);
            Assert.AreEqual("on E2 between S3 & S4, heading east", loc.Description);
            Assert.AreEqual(lane.LaneOffset, loc.LateralOffset, 0.01f);
            Assert.AreEqual("S4 & E2", graph.Nodes[loc.NextNode].Name);
        }

        [Test]
        public void Localizer_DetectsIntersectionAndLandmark()
        {
            var at = Localizer.Locate(graph, CityLayout.Intersection(2, 3), Vector3.forward);
            Assert.AreEqual("at S3 & E4, heading north", at.Description);

            graph.TryGetLandmark("Home", out var home);
            var near = Localizer.Locate(graph, LanePath.CurbPose(home, lane).position, Vector3.right);
            Assert.AreEqual("Home", near.NearLandmark);
        }
    }
}
