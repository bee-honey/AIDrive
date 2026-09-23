using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AIDrive.Navigation
{
    public enum Turn { Start, Straight, Left, Right }

    public class RouteOptions
    {
        /// <summary>Streets to avoid. Segments touching the start or goal are still allowed.</summary>
        public ICollection<string> AvoidStreets;

        /// <summary>Edge the vehicle arrived at the start node on; forbids an immediate U-turn back along it.</summary>
        public int ArrivedViaEdge = -1;

        /// <summary>Extra cost (metres) per turn, so equal-length routes prefer fewer turns.</summary>
        public float TurnPenalty = 20f;

        /// <summary>
        /// Extra cost (metres) for arriving with a landmark on the left, which would force a pull-over across traffic.
        /// Large enough that looping around a block to arrive on the right always wins on this grid.
        /// </summary>
        public float ArrivalLeftPenalty = 400f;

        /// <summary>False turns A* into plain Dijkstra (used by tests to verify optimality).</summary>
        public bool UseHeuristic = true;

        public RouteOptions WithArrivedVia(int edgeId) => new RouteOptions
        {
            AvoidStreets = AvoidStreets,
            ArrivedViaEdge = edgeId,
            TurnPenalty = TurnPenalty,
            ArrivalLeftPenalty = ArrivalLeftPenalty,
            UseHeuristic = UseHeuristic,
        };
    }

    public class RouteLeg
    {
        public string Street;
        public Turn Turn;
        public string Heading;
        public float Length;
        public int StartNode, EndNode;
    }

    public class Route
    {
        public bool Success;
        public string Error;
        public List<int> NodeIds = new List<int>();
        public List<int> EdgeIds = new List<int>();
        public List<RouteLeg> Legs = new List<RouteLeg>();
        public List<string> Directions = new List<string>();
        public float Length;
        /// <summary>Length plus turn penalties — the quantity A* minimises.</summary>
        public float Cost;
        public int Turns;
        public string Summary;
        public int NodesExpanded;

        public static Route Fail(string error) => new Route { Success = false, Error = error, Summary = error };
    }

    /// <summary>
    /// A* over (node, arrived-via-edge) states so that routes never make a U-turn:
    /// a vehicle cannot leave a node along the same edge it arrived on.
    /// </summary>
    public static class RoutePlanner
    {
        public static Route Plan(RoadGraph graph, string from, string to, RouteOptions options = null)
        {
            if (!graph.TryGetLandmark(from, out var start)) return Route.Fail($"Unknown landmark '{from}'");
            if (!graph.TryGetLandmark(to, out var goal)) return Route.Fail($"Unknown landmark '{to}'");
            return Plan(graph, start.Id, goal.Id, options);
        }

        /// <summary>Plans from a vehicle's current pose: starts at the node it is driving toward, with no U-turn back.</summary>
        public static Route PlanFrom(RoadGraph graph, Vector3 position, Vector3 forward, string to,
                                     RouteOptions options, out VehicleLocation location)
        {
            location = Localizer.Locate(graph, position, forward);
            if (!graph.TryGetLandmark(to, out var goal)) return Route.Fail($"Unknown landmark '{to}'");
            return Plan(graph, location.NextNode, goal.Id, (options ?? new RouteOptions()).WithArrivedVia(location.EdgeId));
        }

        public static Route Plan(RoadGraph graph, int startNode, int goalNode, RouteOptions options = null)
        {
            options = options ?? new RouteOptions();
            var avoid = new HashSet<string>(options.AvoidStreets ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var goalPos = graph.Nodes[goalNode].Position;

            if (startNode == goalNode)
            {
                var here = new Route { Success = true, Summary = $"Already at {graph.Nodes[goalNode].Name}" };
                here.NodeIds.Add(startNode);
                here.Directions.Add(here.Summary);
                return here;
            }

            var gScore = new Dictionary<long, float>();
            var cameFrom = new Dictionary<long, long>();
            var closed = new HashSet<long>();
            var open = new MinHeap<long>();

            long startKey = Key(startNode, options.ArrivedViaEdge);
            gScore[startKey] = 0f;
            open.Push(startKey, H(graph, startNode, goalPos, options));
            int expanded = 0;

            while (open.Count > 0)
            {
                long key = open.Pop();
                if (!closed.Add(key)) continue;
                expanded++;

                int node = NodeOf(key);
                int via = EdgeOf(key);
                if (node == goalNode)
                {
                    var route = Reconstruct(graph, cameFrom, key, startKey);
                    route.Cost = gScore[key];
                    route.NodesExpanded = expanded;
                    return route;
                }

                float g = gScore[key];
                foreach (int eid in graph.Nodes[node].Edges)
                {
                    var e = graph.Edges[eid];
                    if (eid == via || e.Blocked) continue;
                    if (avoid.Contains(e.Street) && !e.Touches(startNode) && !e.Touches(goalNode)) continue;

                    int next = e.Other(node);
                    long nextKey = Key(next, eid);
                    float ng = g + e.Length;
                    if (via >= 0 && IsTurn(graph, graph.Edges[via].Other(node), node, next)) ng += options.TurnPenalty;
                    if (next == goalNode && !ArrivesOnRight(graph, node, next)) ng += options.ArrivalLeftPenalty;
                    if (gScore.TryGetValue(nextKey, out var old) && ng >= old) continue;

                    gScore[nextKey] = ng;
                    cameFrom[nextKey] = key;
                    open.Push(nextKey, ng + H(graph, next, goalPos, options));
                }
            }

            var fail = Route.Fail($"No route from {graph.Nodes[startNode].Name} to {graph.Nodes[goalNode].Name}" +
                                  (avoid.Count > 0 ? $" avoiding {string.Join(", ", avoid)}" : ""));
            fail.NodesExpanded = expanded;
            return fail;
        }

        static bool IsTurn(RoadGraph graph, int prev, int node, int next)
        {
            var a = graph.Nodes[node].Position - graph.Nodes[prev].Position;
            var b = graph.Nodes[next].Position - graph.Nodes[node].Position;
            return TurnBetween(a.normalized, b.normalized) != Turn.Straight;
        }

        /// <summary>True if travelling node → landmark puts the landmark's block on the right (always true for intersections).</summary>
        public static bool ArrivesOnRight(RoadGraph graph, int fromNode, int landmarkNode)
        {
            var goal = graph.Nodes[landmarkNode];
            if (goal.Kind != NodeKind.Landmark) return true;
            var travel = goal.Position - graph.Nodes[fromNode].Position;
            return Vector3.Cross(travel, goal.BlockCenter - goal.Position).y > 0f;
        }

        static float H(RoadGraph graph, int node, Vector3 goalPos, RouteOptions o) =>
            o.UseHeuristic ? Vector3.Distance(graph.Nodes[node].Position, goalPos) : 0f;

        static long Key(int node, int viaEdge) => ((long)node << 32) | (uint)(viaEdge + 1);
        static int NodeOf(long key) => (int)(key >> 32);
        static int EdgeOf(long key) => (int)(key & 0xFFFFFFFF) - 1;

        static Route Reconstruct(RoadGraph graph, Dictionary<long, long> cameFrom, long goalKey, long startKey)
        {
            var route = new Route { Success = true };
            for (long k = goalKey; ; k = cameFrom[k])
            {
                route.NodeIds.Add(NodeOf(k));
                if (k == startKey) break;
                route.EdgeIds.Add(EdgeOf(k));
            }
            route.NodeIds.Reverse();
            route.EdgeIds.Reverse();
            route.Length = route.EdgeIds.Sum(e => graph.Edges[e].Length);
            BuildLegs(graph, route);
            route.Turns = route.Legs.Count - 1;
            BuildDirections(graph, route);
            return route;
        }

        static void BuildLegs(RoadGraph graph, Route route)
        {
            RouteLeg leg = null;
            Vector3 prevDir = Vector3.zero;
            for (int k = 0; k < route.EdgeIds.Count; k++)
            {
                var e = graph.Edges[route.EdgeIds[k]];
                int a = route.NodeIds[k], b = route.NodeIds[k + 1];
                var dir = (graph.Nodes[b].Position - graph.Nodes[a].Position).normalized;

                if (leg == null || leg.Street != e.Street)
                {
                    leg = new RouteLeg
                    {
                        Street = e.Street,
                        Turn = leg == null ? Turn.Start : TurnBetween(prevDir, dir),
                        Heading = Heading(dir),
                        StartNode = a,
                    };
                    route.Legs.Add(leg);
                }
                leg.Length += e.Length;
                leg.EndNode = b;
                prevDir = dir;
            }
        }

        static void BuildDirections(RoadGraph graph, Route route)
        {
            foreach (var leg in route.Legs)
            {
                string text = leg.Turn == Turn.Start
                    ? $"Head {leg.Heading} on {leg.Street}"
                    : $"At {graph.Nodes[leg.StartNode].Name}, turn {leg.Turn.ToString().ToLower()} onto {leg.Street} heading {leg.Heading}";
                route.Directions.Add($"{text} for {leg.Length:0} m");
            }

            var goal = graph.Nodes[route.NodeIds[route.NodeIds.Count - 1]];
            string arrive = $"Arrive at {goal.Name}";
            if (goal.Kind == NodeKind.Landmark && route.NodeIds.Count >= 2)
            {
                int prev = route.NodeIds[route.NodeIds.Count - 2];
                arrive += ArrivesOnRight(graph, prev, goal.Id) ? " on your right" : " on your left";
            }
            route.Directions.Add(arrive);
            route.Summary = $"{string.Join(" → ", route.Legs.Select(l => l.Street))} ({route.Length:0} m)";
        }

        /// <summary>Unity is left-handed with +Y up: a positive cross.y means the new direction is to the right.</summary>
        static Turn TurnBetween(Vector3 from, Vector3 to)
        {
            float cross = Vector3.Cross(from, to).y;
            if (Mathf.Abs(cross) < 0.1f) return Turn.Straight;
            return cross > 0 ? Turn.Right : Turn.Left;
        }

        public static string Heading(Vector3 dir)
        {
            if (Mathf.Abs(dir.x) > Mathf.Abs(dir.z)) return dir.x > 0 ? "east" : "west";
            return dir.z > 0 ? "north" : "south";
        }

        /// <summary>Minimal binary min-heap; .NET Standard 2.1 has no PriorityQueue.</summary>
        class MinHeap<T>
        {
            readonly List<(T item, float priority)> heap = new List<(T, float)>();
            public int Count => heap.Count;

            public void Push(T item, float priority)
            {
                heap.Add((item, priority));
                int i = heap.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (heap[parent].priority <= heap[i].priority) break;
                    (heap[parent], heap[i]) = (heap[i], heap[parent]);
                    i = parent;
                }
            }

            public T Pop()
            {
                var top = heap[0].item;
                heap[0] = heap[heap.Count - 1];
                heap.RemoveAt(heap.Count - 1);
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = l + 1, min = i;
                    if (l < heap.Count && heap[l].priority < heap[min].priority) min = l;
                    if (r < heap.Count && heap[r].priority < heap[min].priority) min = r;
                    if (min == i) break;
                    (heap[min], heap[i]) = (heap[i], heap[min]);
                    i = min;
                }
                return top;
            }
        }
    }
}
