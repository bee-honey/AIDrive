using System.Collections.Generic;
using AIDrive.City;
using UnityEngine;

namespace AIDrive.Navigation
{
    public class Maneuver
    {
        /// <summary>Distance along the path where the maneuver starts.</summary>
        public float Distance;
        public string Text;
    }

    public struct LaneSettings
    {
        /// <summary>Right-lane centre, measured from the road centre line.</summary>
        public float LaneOffset;
        /// <summary>Curb-lane centre used for pulling over at the destination.</summary>
        public float CurbOffset;
        /// <summary>How far before/after an intersection centre a turn curve starts/ends.</summary>
        public float TurnEntry;
        public float Spacing;

        public static LaneSettings Default => new LaneSettings { LaneOffset = 1.8f, CurbOffset = 4.3f, TurnEntry = 10f, Spacing = 1f };
    }

    /// <summary>
    /// A route turned into a drivable polyline: right-hand lane, Bézier curves through turns,
    /// and a pull-over into the curb lane at the destination. Points are evenly spaced.
    /// </summary>
    public class LanePath
    {
        const float MergeDistance = 12f;

        public readonly List<Vector3> Points = new List<Vector3>();
        public readonly List<float> Distances = new List<float>();
        public readonly List<Maneuver> Maneuvers = new List<Maneuver>();

        public float Length => Distances.Count > 0 ? Distances[Distances.Count - 1] : 0f;

        /// <summary>Right-hand normal of a travel direction (Unity: +Y up, left-handed).</summary>
        public static Vector3 RightOf(Vector3 dir) => new Vector3(dir.z, 0f, -dir.x);

        /// <summary>Parked pose at a landmark's drop-off zone, facing so the landmark is on the right.</summary>
        public static Pose CurbPose(RoadNode landmark, LaneSettings s)
        {
            var toBlock = landmark.BlockCenter - landmark.Position;
            toBlock = Mathf.Abs(toBlock.x) > Mathf.Abs(toBlock.z)
                ? new Vector3(Mathf.Sign(toBlock.x), 0, 0)
                : new Vector3(0, 0, Mathf.Sign(toBlock.z));
            var dir = new Vector3(-toBlock.z, 0f, toBlock.x); // RightOf(dir) == toBlock
            return new Pose(landmark.Position + toBlock * s.CurbOffset, Quaternion.LookRotation(dir));
        }

        /// <param name="startDir">Direction of the edge the vehicle is on, toward the route's first node.</param>
        public static LanePath Build(RoadGraph graph, Route route, Vector3 startPos, Vector3 startDir, LaneSettings s)
        {
            var raw = new List<Vector3> { Flat(startPos) };
            var rawManeuvers = new List<(int index, string text)>();
            var dIn = Flat(startDir).normalized;

            // Pulling out from the curb (or otherwise off-lane): merge into the driving lane within MergeDistance.
            var first = graph.Nodes[route.NodeIds[0]].Position;
            float ahead = Vector3.Dot(first - raw[0], dIn);
            float lateral = Vector3.Dot(raw[0] - first, RightOf(dIn));
            if (Mathf.Abs(lateral - s.LaneOffset) > 0.5f && ahead > MergeDistance + s.TurnEntry)
                raw.Add(first - dIn * (ahead - MergeDistance) + RightOf(dIn) * s.LaneOffset);

            for (int k = 0; k < route.NodeIds.Count; k++)
            {
                var node = graph.Nodes[route.NodeIds[k]];
                var n = node.Position;
                var rIn = RightOf(dIn);

                if (k == route.NodeIds.Count - 1)
                {
                    // Pull over into the curb lane along a smooth S-curve, stopping just past the landmark.
                    const float pullStart = 15f, pullEnd = 1f;
                    for (int i = 0; i <= 14; i++)
                    {
                        float t = i / 14f;
                        float offset = Mathf.Lerp(s.LaneOffset, s.CurbOffset, t * t * (3f - 2f * t));
                        raw.Add(n - dIn * Mathf.Lerp(pullStart, pullEnd, t) + rIn * offset);
                    }
                    raw.Add(n + dIn * 2f + rIn * s.CurbOffset);
                    break;
                }

                var dOut = (graph.Nodes[route.NodeIds[k + 1]].Position - n).normalized;
                float cross = Vector3.Cross(dIn, dOut).y;
                if (Mathf.Abs(cross) < 0.1f)
                {
                    raw.Add(n + rIn * s.LaneOffset);
                }
                else
                {
                    var rOut = RightOf(dOut);
                    var entry = n + rIn * s.LaneOffset - dIn * s.TurnEntry;
                    var corner = n + (rIn + rOut) * s.LaneOffset;
                    var exit = n + rOut * s.LaneOffset + dOut * s.TurnEntry;
                    var street = graph.FindEdge(node.Id, route.NodeIds[k + 1]).Street;
                    rawManeuvers.Add((raw.Count, $"Turn {(cross > 0 ? "right" : "left")} onto {street}"));
                    const int samples = 12;
                    for (int i = 0; i <= samples; i++)
                    {
                        float t = i / (float)samples;
                        raw.Add((1 - t) * (1 - t) * entry + 2 * (1 - t) * t * corner + t * t * exit);
                    }
                }
                dIn = dOut;
            }

            // Drop leading points the vehicle has already passed.
            var start = raw[0];
            var startFwd = Flat(startDir).normalized;
            int removed = 0;
            while (raw.Count > 2 && Vector3.Dot(raw[1] - start, startFwd) < 0.5f)
            {
                raw.RemoveAt(1);
                removed++;
            }

            var path = Resample(raw, s.Spacing, out var rawDistances);
            foreach (var (index, text) in rawManeuvers)
            {
                int i = Mathf.Clamp(index - removed, 0, rawDistances.Count - 1);
                path.Maneuvers.Add(new Maneuver { Distance = rawDistances[i], Text = text });
            }

            var goal = graph.Nodes[route.NodeIds[route.NodeIds.Count - 1]];
            path.Maneuvers.Add(new Maneuver { Distance = path.Length, Text = $"Arrive at {goal.Name}" });
            return path;
        }

        /// <summary>Index of the path point nearest to <paramref name="p"/>, searching forward from <paramref name="from"/>.</summary>
        public int ClosestIndex(Vector3 p, int from, int window)
        {
            p = Flat(p);
            int best = from;
            float bestDist = float.MaxValue;
            int end = Mathf.Min(Points.Count - 1, from + window);
            for (int i = Mathf.Max(0, from); i <= end; i++)
            {
                float d = (Points[i] - p).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }
            return best;
        }

        public Vector3 PointAtDistance(float s)
        {
            if (s <= 0f) return Points[0];
            if (s >= Length) return Points[Points.Count - 1];
            int lo = 0, hi = Distances.Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (Distances[mid] <= s) lo = mid; else hi = mid;
            }
            float t = (s - Distances[lo]) / Mathf.Max(1e-4f, Distances[hi] - Distances[lo]);
            return Vector3.Lerp(Points[lo], Points[hi], t);
        }

        static LanePath Resample(List<Vector3> raw, float spacing, out List<float> rawDistances)
        {
            rawDistances = new List<float> { 0f };
            for (int i = 1; i < raw.Count; i++)
                rawDistances.Add(rawDistances[i - 1] + Vector3.Distance(raw[i - 1], raw[i]));

            var path = new LanePath();
            float total = rawDistances[rawDistances.Count - 1];
            int seg = 0;
            for (float s = 0f; s < total; s += spacing)
            {
                while (seg < raw.Count - 2 && rawDistances[seg + 1] < s) seg++;
                float len = Mathf.Max(1e-4f, rawDistances[seg + 1] - rawDistances[seg]);
                path.Points.Add(Vector3.Lerp(raw[seg], raw[seg + 1], (s - rawDistances[seg]) / len));
                path.Distances.Add(s);
            }
            path.Points.Add(raw[raw.Count - 1]);
            path.Distances.Add(total);
            return path;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
