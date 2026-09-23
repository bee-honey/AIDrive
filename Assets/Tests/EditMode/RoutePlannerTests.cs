using System;
using System.Linq;
using AIDrive.City;
using AIDrive.Navigation;
using NUnit.Framework;
using UnityEngine;

namespace AIDrive.Tests
{
    public class RoutePlannerTests
    {
        RoadGraph graph;

        [SetUp]
        public void SetUp() => graph = RoadGraph.BuildDefault();

        [Test]
        public void Graph_HasExpectedShape()
        {
            int landmarks = CityLayout.DefaultLandmarks.Length;
            int gridEdges = 2 * CityLayout.RoadCount * CityLayout.Blocks;
            Assert.AreEqual(CityLayout.RoadCount * CityLayout.RoadCount + landmarks, graph.Nodes.Count);
            Assert.AreEqual(gridEdges + landmarks, graph.Edges.Count, "each landmark splits one segment into two");
            Assert.AreEqual(landmarks, graph.Landmarks.Count());
        }

        [Test]
        public void Graph_UsesGridStreetNames()
        {
            Assert.AreEqual("S1 & E1", graph.Intersection(0, 0).Name);
            Assert.AreEqual("S4 & E5", graph.Intersection(3, 4).Name);
            var streets = graph.Edges.Select(e => e.Street).Distinct().OrderBy(s => s).ToArray();
            Assert.AreEqual(2 * CityLayout.RoadCount, streets.Length);
        }

        [Test]
        public void Graph_LandmarkLookupIsCaseInsensitive()
        {
            Assert.IsTrue(graph.TryGetLandmark("park b", out var node));
            Assert.AreEqual("Park B", node.Name);
            Assert.IsFalse(graph.TryGetLandmark("Airport", out _));
        }

        [Test]
        public void Graph_RejectsLandmarksSharingASegment()
        {
            var defs = new[]
            {
                new LandmarkDef("A", 0, 0, RoadSide.North, Color.white),
                new LandmarkDef("B", 0, 1, RoadSide.South, Color.white), // same road segment from the other side
            };
            Assert.Throws<ArgumentException>(() => RoadGraph.Build(defs));
        }

        [Test]
        public void EveryLandmarkPair_HasAValidRoute()
        {
            var names = graph.Landmarks.Select(n => n.Name).ToList();
            foreach (var from in names)
            foreach (var to in names)
            {
                if (from == to) continue;
                var r = RoutePlanner.Plan(graph, from, to);
                Assert.IsTrue(r.Success, $"{from} → {to}: {r.Error}");
                AssertRouteIsConnected(r);
                AssertNoUTurns(r);
                Assert.AreEqual(from, graph.Nodes[r.NodeIds.First()].Name);
                Assert.AreEqual(to, graph.Nodes[r.NodeIds.Last()].Name);
            }
        }

        [Test]
        public void AStar_MatchesDijkstraOptimalLength()
        {
            var names = graph.Landmarks.Select(n => n.Name).ToList();
            foreach (var from in names)
            foreach (var to in names)
            {
                if (from == to) continue;
                var astar = RoutePlanner.Plan(graph, from, to);
                var dijkstra = RoutePlanner.Plan(graph, from, to, new RouteOptions { UseHeuristic = false });
                Assert.AreEqual(dijkstra.Cost, astar.Cost, 0.01f, $"{from} → {to}");
                Assert.LessOrEqual(astar.NodesExpanded, dijkstra.NodesExpanded, $"{from} → {to}: heuristic should not expand more");
            }
        }

        [Test]
        public void TurnPenalty_PrefersFewerTurnsAtEqualLength()
        {
            var straightest = RoutePlanner.Plan(graph, "Home", "Hospital");
            var anyShortest = RoutePlanner.Plan(graph, "Home", "Hospital", new RouteOptions { TurnPenalty = 0f });
            Assert.AreEqual(anyShortest.Length, straightest.Length, 0.01f, "penalty must not lengthen this route");
            Assert.LessOrEqual(straightest.Turns, 2, "a Manhattan route needs at most 2 turns here");
        }

        [Test]
        public void BlockedEdge_IsAvoided()
        {
            var baseline = RoutePlanner.Plan(graph, "Home", "Hospital");
            var blocked = baseline.EdgeIds[baseline.EdgeIds.Count / 2];
            graph.SetBlocked(blocked, true);

            var detour = RoutePlanner.Plan(graph, "Home", "Hospital");
            Assert.IsTrue(detour.Success);
            CollectionAssert.DoesNotContain(detour.EdgeIds, blocked);
            Assert.GreaterOrEqual(detour.Length, baseline.Length - 0.01f);
        }

        [Test]
        public void AvoidStreet_IsRespectedExceptAtEndpoints()
        {
            var r = RoutePlanner.Plan(graph, "Home", "Hospital", new RouteOptions { AvoidStreets = new[] { "E2" } });
            Assert.IsTrue(r.Success);
            int start = r.NodeIds.First(), goal = r.NodeIds.Last();
            foreach (var eid in r.EdgeIds)
            {
                var e = graph.Edges[eid];
                if (e.Street == "E2")
                    Assert.IsTrue(e.Touches(start) || e.Touches(goal), "E2 used away from the endpoints");
            }
        }

        [Test]
        public void UnreachableGoal_FailsWithMessage()
        {
            graph.TryGetLandmark("Office", out var office);
            foreach (var e in office.Edges) graph.SetBlocked(e, true);

            var r = RoutePlanner.Plan(graph, "Home", "Office");
            Assert.IsFalse(r.Success);
            StringAssert.Contains("No route", r.Error);
        }

        [Test]
        public void UnknownLandmark_Fails()
        {
            var r = RoutePlanner.Plan(graph, "Home", "Airport");
            Assert.IsFalse(r.Success);
            StringAssert.Contains("Unknown landmark", r.Error);
        }

        [Test]
        public void ArrivedViaEdge_ForbidsImmediateUTurn()
        {
            // A car at Home that arrived heading east must not leave westward.
            graph.TryGetLandmark("Home", out var home);
            var westEdge = home.Edges.Select(e => graph.Edges[e])
                .First(e => graph.Nodes[e.Other(home.Id)].Position.x < home.Position.x);
            var eastEdge = home.Edges.First(e => e != westEdge.Id);

            var r = RoutePlanner.Plan(graph, home.Id, graph.Intersection(0, 1).Id,
                new RouteOptions { ArrivedViaEdge = westEdge.Id });
            Assert.IsTrue(r.Success);
            Assert.AreEqual(eastEdge, r.EdgeIds.First());
        }

        [Test]
        public void Directions_DescribeTurnsAndArrival()
        {
            var r = RoutePlanner.Plan(graph, "Home", "Park B");
            Assert.IsTrue(r.Success);
            StringAssert.StartsWith("Head ", r.Directions.First());
            StringAssert.StartsWith("Arrive at Park B on your", r.Directions.Last());
            Assert.AreEqual(Turn.Start, r.Legs[0].Turn);
            Assert.IsTrue(r.Legs.Skip(1).All(l => l.Turn == Turn.Left || l.Turn == Turn.Right));
        }

        void AssertRouteIsConnected(Route r)
        {
            Assert.AreEqual(r.NodeIds.Count, r.EdgeIds.Count + 1);
            for (int k = 0; k < r.EdgeIds.Count; k++)
            {
                var e = graph.Edges[r.EdgeIds[k]];
                Assert.IsTrue(e.Touches(r.NodeIds[k]) && e.Touches(r.NodeIds[k + 1]));
            }
        }

        static void AssertNoUTurns(Route r)
        {
            for (int k = 1; k < r.EdgeIds.Count; k++)
                Assert.AreNotEqual(r.EdgeIds[k - 1], r.EdgeIds[k], "route reverses along the same edge");
        }
    }
}
