using System.Text.RegularExpressions;
using AIDrive.City;
using UnityEngine;

namespace AIDrive.Navigation
{
    public enum LanePosition { Inner, Curb, Both }

    public struct RoadSpot
    {
        public Vector3 Position;
        /// <summary>Direction of travel for the addressed lanes.</summary>
        public Vector3 Direction;
        public Vector3 Right;
    }

    /// <summary>
    /// Resolves human-readable road addresses — "E2 between S3 and S4, eastbound, inner lane, halfway" —
    /// to world positions. Scenarios, evals and the agent all speak in these terms.
    /// </summary>
    public static class RoadAddress
    {
        /// <summary>Lane centres measured from the road centre line (lane dividers sit at 3 m, curb at 6 m).</summary>
        public const float InnerLane = 1.6f, CurbLane = 4.4f, BothLanes = 3.0f;

        static readonly Regex StreetPattern = new Regex(@"^([ES])([1-9])$");

        public static float LaneOffset(LanePosition lane) =>
            lane == LanePosition.Inner ? InnerLane : lane == LanePosition.Curb ? CurbLane : BothLanes;

        public static bool TryParseLane(string s, out LanePosition lane)
        {
            switch ((s ?? "inner").Trim().ToLowerInvariant())
            {
                case "":
                case "inner": lane = LanePosition.Inner; return true;
                case "curb": lane = LanePosition.Curb; return true;
                case "both": lane = LanePosition.Both; return true;
                default: lane = LanePosition.Inner; return false;
            }
        }

        /// <param name="at">0 … 1 along the block in the direction of travel (intersections excluded).</param>
        public static bool TryResolve(string street, string crossA, string crossB, string direction,
                                      LanePosition lane, float at, out RoadSpot spot, out string error)
        {
            spot = default;
            if (!TryParseStreet(street, out bool eastWest, out int index))
            {
                error = $"Unknown street '{street}'";
                return false;
            }
            if (!TryParseStreet(crossA, out bool aEW, out int a) || !TryParseStreet(crossB, out bool bEW, out int b) ||
                aEW == eastWest || bEW == eastWest)
            {
                error = $"'{crossA}' and '{crossB}' must be streets crossing {street}";
                return false;
            }
            if (Mathf.Abs(a - b) != 1)
            {
                error = $"{crossA} and {crossB} are not adjacent cross streets of {street}";
                return false;
            }

            Vector3 dir;
            switch ((direction ?? "").Trim().ToLowerInvariant())
            {
                case "eastbound": dir = Vector3.right; break;
                case "westbound": dir = Vector3.left; break;
                case "northbound": dir = Vector3.forward; break;
                case "southbound": dir = Vector3.back; break;
                default:
                    error = $"Unknown direction '{direction}' (eastbound/westbound/northbound/southbound)";
                    return false;
            }
            if (eastWest != (Mathf.Abs(dir.x) > 0f))
            {
                error = $"{street} runs {(eastWest ? "east–west" : "north–south")}, it has no {direction} lanes";
                return false;
            }

            // The block starts at whichever cross street the traffic reaches first.
            bool increasing = eastWest ? dir.x > 0f : dir.z > 0f;
            int first = increasing ? Mathf.Min(a, b) : Mathf.Max(a, b);
            var start = eastWest ? CityLayout.Intersection(first, index) : CityLayout.Intersection(index, first);
            float along = CityLayout.RoadWidth / 2f + 1f + Mathf.Clamp01(at) * (CityLayout.BlockSize - 2f);
            var right = LanePath.RightOf(dir);

            spot = new RoadSpot { Position = start + dir * along + right * LaneOffset(lane), Direction = dir, Right = right };
            error = null;
            return true;
        }

        /// <summary>"E3" → east–west road index 2; "S1" → north–south road index 0.</summary>
        public static bool TryParseStreet(string s, out bool eastWest, out int index)
        {
            eastWest = false;
            index = -1;
            var m = StreetPattern.Match((s ?? "").Trim().ToUpperInvariant());
            if (!m.Success) return false;
            eastWest = m.Groups[1].Value == "E";
            index = int.Parse(m.Groups[2].Value) - 1;
            return index < CityLayout.RoadCount;
        }
    }
}
