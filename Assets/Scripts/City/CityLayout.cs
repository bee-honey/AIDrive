using System;
using UnityEngine;

namespace AIDrive.City
{
    /// <summary>Which side of a block a landmark faces (the road it is accessed from).</summary>
    public enum RoadSide { North, East, South, West }

    [Serializable]
    public struct LandmarkDef
    {
        public string name;
        public int blockX;
        public int blockZ;
        public RoadSide side;
        public Color color;

        public LandmarkDef(string name, int blockX, int blockZ, RoadSide side, Color color)
        {
            this.name = name;
            this.blockX = blockX;
            this.blockZ = blockZ;
            this.side = side;
            this.color = color;
        }
    }

    /// <summary>
    /// Single source of truth for the city grid. Both the city generator and the road graph
    /// read from here so they can never disagree.
    ///
    /// Conventions: +Z is north, +X is east.
    /// North–south roads are named S1..S7 (west → east), east–west roads E1..E7 (south → north).
    /// Intersection (i, j) is where S(i+1) crosses E(j+1). Block (bx, bz) sits between
    /// roads S(bx+1)/S(bx+2) and E(bz+1)/E(bz+2).
    /// </summary>
    public static class CityLayout
    {
        public const int Blocks = 6;
        public const int RoadCount = Blocks + 1;
        public const float BlockSize = 40f;
        public const float RoadWidth = 12f;
        public const float SidewalkHeight = 0.2f;

        public static float Pitch => BlockSize + RoadWidth;
        public static float Extent => Blocks * BlockSize + RoadCount * RoadWidth;
        public static float Origin => -Extent / 2f;

        /// <summary>Centre-line coordinate of road index i (x for S roads, z for E roads).</summary>
        public static float RoadCenter(int i) => Origin + RoadWidth / 2f + i * Pitch;

        public static Vector3 Intersection(int i, int j) => new Vector3(RoadCenter(i), 0f, RoadCenter(j));

        public static Vector3 BlockCenter(int bx, int bz) =>
            new Vector3(Origin + RoadWidth + BlockSize / 2f + bx * Pitch, 0f,
                        Origin + RoadWidth + BlockSize / 2f + bz * Pitch);

        public static string NorthSouthRoad(int i) => "S" + (i + 1);
        public static string EastWestRoad(int j) => "E" + (j + 1);
        public static string IntersectionName(int i, int j) => NorthSouthRoad(i) + " & " + EastWestRoad(j);

        /// <summary>
        /// Traffic signals only where main roads cross: S2/S4/S6 × E2/E4/E6 (9 intersections).
        /// Every other intersection is unsignalised.
        /// </summary>
        public static bool IsSignalized(int i, int j) => i % 2 == 1 && j % 2 == 1 && i < RoadCount - 1 && j < RoadCount - 1;

        public static readonly LandmarkDef[] DefaultLandmarks =
        {
            new LandmarkDef("Home",        0, 0, RoadSide.North, new Color(0.20f, 0.50f, 0.95f)),
            new LandmarkDef("School",      0, 5, RoadSide.East,  new Color(0.95f, 0.75f, 0.10f)),
            new LandmarkDef("Park A",      1, 4, RoadSide.South, new Color(0.25f, 0.75f, 0.30f)),
            new LandmarkDef("Mall",        2, 1, RoadSide.North, new Color(0.80f, 0.30f, 0.75f)),
            new LandmarkDef("Office",      3, 3, RoadSide.West,  new Color(0.10f, 0.75f, 0.80f)),
            new LandmarkDef("Park B",      4, 1, RoadSide.East,  new Color(0.25f, 0.75f, 0.30f)),
            new LandmarkDef("Gas Station", 5, 0, RoadSide.West,  new Color(1.00f, 0.50f, 0.10f)),
            new LandmarkDef("Hospital",    5, 5, RoadSide.South, new Color(0.90f, 0.15f, 0.15f)),
        };
    }
}
