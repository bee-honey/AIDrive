using System;
using System.Collections.Generic;
using AIDrive.City;
using UnityEngine;

namespace AIDrive.Navigation
{
    public enum NodeKind { Intersection, Landmark }

    public class RoadNode
    {
        public int Id;
        public string Name;
        public NodeKind Kind;
        public Vector3 Position;
        public readonly List<int> Edges = new List<int>();

        /// <summary>Grid coordinates for intersections, -1 for landmarks.</summary>
        public int GridX = -1, GridZ = -1;

        /// <summary>Landmarks only: centre of the block the landmark belongs to.</summary>
        public Vector3 BlockCenter;
    }

    public class RoadEdge
    {
        public int Id;
        public int A, B;
        public string Street;
        public float Length;
        public bool Blocked;

        public int Other(int node) => node == A ? B : A;
        public bool Touches(int node) => node == A || node == B;
    }

    /// <summary>
    /// Undirected road network: a node at every intersection and landmark, an edge per road
    /// segment between them. Built from <see cref="CityLayout"/> plus a set of landmarks.
    /// </summary>
    public class RoadGraph
    {
        readonly List<RoadNode> nodes = new List<RoadNode>();
        readonly List<RoadEdge> edges = new List<RoadEdge>();
        readonly Dictionary<string, int> landmarkIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        readonly int[,] intersectionIds = new int[CityLayout.RoadCount, CityLayout.RoadCount];

        public IReadOnlyList<RoadNode> Nodes => nodes;
        public IReadOnlyList<RoadEdge> Edges => edges;

        public IEnumerable<RoadNode> Landmarks
        {
            get { foreach (var n in nodes) if (n.Kind == NodeKind.Landmark) yield return n; }
        }

        public RoadNode Intersection(int i, int j) => nodes[intersectionIds[i, j]];

        public bool TryGetLandmark(string name, out RoadNode node)
        {
            node = null;
            if (name == null || !landmarkIds.TryGetValue(name.Trim(), out var id)) return false;
            node = nodes[id];
            return true;
        }

        public RoadEdge FindEdge(int a, int b)
        {
            foreach (var e in nodes[a].Edges)
                if (edges[e].Other(a) == b) return edges[e];
            return null;
        }

        public void SetBlocked(int edgeId, bool blocked) => edges[edgeId].Blocked = blocked;

        public void ClearBlocked()
        {
            foreach (var e in edges) e.Blocked = false;
        }

        public static RoadGraph Build(IEnumerable<LandmarkDef> landmarks)
        {
            var g = new RoadGraph();
            int n = CityLayout.RoadCount;

            for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                var node = g.AddNode(CityLayout.IntersectionName(i, j), NodeKind.Intersection, CityLayout.Intersection(i, j));
                node.GridX = i;
                node.GridZ = j;
                g.intersectionIds[i, j] = node.Id;
            }

            // Assign each landmark to the road segment it fronts.
            var bySegment = new Dictionary<string, LandmarkDef>();
            foreach (var def in landmarks)
            {
                if (string.IsNullOrWhiteSpace(def.name))
                    throw new ArgumentException("Landmark name must not be empty");
                if (def.blockX < 0 || def.blockX >= CityLayout.Blocks || def.blockZ < 0 || def.blockZ >= CityLayout.Blocks)
                    throw new ArgumentException($"Landmark '{def.name}' is outside the city grid");
                if (g.landmarkIds.ContainsKey(def.name))
                    throw new ArgumentException($"Duplicate landmark name '{def.name}'");
                g.landmarkIds[def.name] = -1; // reserve; real id assigned when the segment is built

                var key = SegmentKey(def);
                if (bySegment.ContainsKey(key))
                    throw new ArgumentException($"Landmarks '{bySegment[key].name}' and '{def.name}' share a road segment");
                bySegment[key] = def;
            }

            for (int j = 0; j < n; j++)
            for (int i = 0; i < n - 1; i++)
                g.AddSegment(g.intersectionIds[i, j], g.intersectionIds[i + 1, j], CityLayout.EastWestRoad(j), bySegment, $"EW{j}_{i}");

            for (int i = 0; i < n; i++)
            for (int j = 0; j < n - 1; j++)
                g.AddSegment(g.intersectionIds[i, j], g.intersectionIds[i, j + 1], CityLayout.NorthSouthRoad(i), bySegment, $"NS{i}_{j}");

            return g;
        }

        public static RoadGraph BuildDefault() => Build(CityLayout.DefaultLandmarks);

        /// <summary>Key of the road segment a landmark fronts, matching the keys used in Build.</summary>
        static string SegmentKey(LandmarkDef d)
        {
            switch (d.side)
            {
                case RoadSide.South: return $"EW{d.blockZ}_{d.blockX}";
                case RoadSide.North: return $"EW{d.blockZ + 1}_{d.blockX}";
                case RoadSide.West:  return $"NS{d.blockX}_{d.blockZ}";
                default:             return $"NS{d.blockX + 1}_{d.blockZ}";
            }
        }

        RoadNode AddNode(string name, NodeKind kind, Vector3 pos)
        {
            var node = new RoadNode { Id = nodes.Count, Name = name, Kind = kind, Position = pos };
            nodes.Add(node);
            return node;
        }

        void AddEdge(int a, int b, string street)
        {
            var e = new RoadEdge
            {
                Id = edges.Count, A = a, B = b, Street = street,
                Length = Vector3.Distance(nodes[a].Position, nodes[b].Position)
            };
            edges.Add(e);
            nodes[a].Edges.Add(e.Id);
            nodes[b].Edges.Add(e.Id);
        }

        void AddSegment(int a, int b, string street, Dictionary<string, LandmarkDef> bySegment, string key)
        {
            if (!bySegment.TryGetValue(key, out var def))
            {
                AddEdge(a, b, street);
                return;
            }

            var mid = (nodes[a].Position + nodes[b].Position) / 2f;
            var lm = AddNode(def.name, NodeKind.Landmark, mid);
            lm.BlockCenter = CityLayout.BlockCenter(def.blockX, def.blockZ);
            landmarkIds[def.name] = lm.Id;
            AddEdge(a, lm.Id, street);
            AddEdge(lm.Id, b, street);
        }
    }
}
