using AIDrive.City;
using UnityEngine;

namespace AIDrive.Navigation
{
    /// <summary>Where a vehicle is on the road network, in terms a human (or an LLM) can read.</summary>
    public class VehicleLocation
    {
        public int EdgeId;
        public int FromNode;
        public int NextNode;
        public string Street;
        public string Heading;
        /// <summary>Signed distance from the road centre line; positive = right of travel direction.</summary>
        public float LateralOffset;
        public float DistanceToNext;
        /// <summary>Intersection name when the vehicle is inside one, otherwise null.</summary>
        public string AtIntersection;
        /// <summary>Landmark name when the vehicle is next to one, otherwise null.</summary>
        public string NearLandmark;
        public string Description;

        public override string ToString() => Description;
    }

    public static class Localizer
    {
        const float LandmarkRadius = 12f;

        public static VehicleLocation Locate(RoadGraph graph, Vector3 position, Vector3 forward)
        {
            var pos = Flat(position);
            var fwd = Flat(forward).normalized;

            // Nearest edge; inside intersections several edges are equally close, so prefer the one aligned with heading.
            RoadEdge best = null;
            float bestScore = float.MaxValue;
            foreach (var e in graph.Edges)
            {
                var a = graph.Nodes[e.A].Position;
                var b = graph.Nodes[e.B].Position;
                var ab = b - a;
                float t = Mathf.Clamp01(Vector3.Dot(pos - a, ab) / ab.sqrMagnitude);
                float dist = Vector3.Distance(pos, a + ab * t);
                float score = dist + (1f - Mathf.Abs(Vector3.Dot(fwd, ab.normalized))) * 3f;
                // Standing exactly on a node, both adjoining edges tie: prefer the one still ahead of us.
                var ahead = Vector3.Dot(fwd, ab) >= 0f ? b : a;
                if (Vector3.Dot(ahead - pos, fwd) < 0.5f) score += 1f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = e;
                }
            }

            var pa = graph.Nodes[best.A].Position;
            var pb = graph.Nodes[best.B].Position;
            bool towardB = Vector3.Dot(fwd, pb - pa) >= 0f;
            int from = towardB ? best.A : best.B;
            int next = towardB ? best.B : best.A;
            var dir = (graph.Nodes[next].Position - graph.Nodes[from].Position).normalized;
            var right = new Vector3(dir.z, 0f, -dir.x);

            var loc = new VehicleLocation
            {
                EdgeId = best.Id,
                FromNode = from,
                NextNode = next,
                Street = best.Street,
                Heading = RoutePlanner.Heading(dir),
                LateralOffset = Vector3.Dot(pos - graph.Nodes[from].Position, right),
                DistanceToNext = Vector3.Dot(graph.Nodes[next].Position - pos, dir),
            };

            foreach (var n in graph.Nodes)
            {
                float d = Vector3.Distance(pos, n.Position);
                if (n.Kind == NodeKind.Intersection && d < CityLayout.RoadWidth / 2f) loc.AtIntersection = n.Name;
                if (n.Kind == NodeKind.Landmark && d < LandmarkRadius) loc.NearLandmark = n.Name;
            }

            loc.Description = loc.AtIntersection != null
                ? $"at {loc.AtIntersection}, heading {loc.Heading}"
                : $"on {loc.Street} between {CrossStreets(best.Street, pos)}, heading {loc.Heading}";
            if (loc.NearLandmark != null) loc.Description += $" (at {loc.NearLandmark})";
            return loc;
        }

        /// <summary>"S3 & S4" for a position on an E road, "E2 & E3" for a position on an S road.</summary>
        static string CrossStreets(string street, Vector3 pos)
        {
            bool eastWest = street.StartsWith("E");
            float coord = eastWest ? pos.x : pos.z;
            int i = Mathf.Clamp(Mathf.FloorToInt((coord - CityLayout.RoadCenter(0)) / CityLayout.Pitch), 0, CityLayout.Blocks - 1);
            return eastWest
                ? $"{CityLayout.NorthSouthRoad(i)} & {CityLayout.NorthSouthRoad(i + 1)}"
                : $"{CityLayout.EastWestRoad(i)} & {CityLayout.EastWestRoad(i + 1)}";
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
