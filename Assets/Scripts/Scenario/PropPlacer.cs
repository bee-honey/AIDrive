using System;
using System.Collections.Generic;
using AIDrive.City;
using AIDrive.Navigation;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AIDrive.Scenario
{
    /// <summary>Spawns props from the <see cref="PropLibrary"/>, by world pose or by road address.</summary>
    public static class PropPlacer
    {
        public const string RootName = "ScenarioProps";

        public static GameObject Spawn(ObstacleKind kind, Vector3 position, Vector3 travelDir, int variant = 0)
        {
            var library = PropLibrary.Load();
            if (library == null) throw new InvalidOperationException("PropLibrary missing — run AIDrive → Build Props");

            // Barricades face oncoming traffic; parked cars face the way traffic flows.
            var facing = kind == ObstacleKind.ParkedCar ? travelDir : -travelDir;
            var go = Object.Instantiate(library.Prefab(kind, variant), new Vector3(position.x, 0f, position.z),
                                        Quaternion.LookRotation(facing), Root());
            go.name = kind.ToString();
            return go;
        }

        /// <summary>Places one placement (possibly a row). Returns false with an error for invalid addresses.</summary>
        public static bool TryPlace(PropPlacement p, List<GameObject> spawned, out string error)
        {
            if (!Enum.TryParse(p.type, true, out ObstacleKind kind))
            {
                error = $"Unknown prop type '{p.type}'";
                return false;
            }
            if (!RoadAddress.TryParseLane(p.lane, out var lane))
            {
                error = $"Unknown lane '{p.lane}' (inner/curb/both)";
                return false;
            }
            if (p.between == null || p.between.Length != 2)
            {
                error = $"{p.type} on {p.street}: 'between' needs exactly two cross streets";
                return false;
            }

            int count = Mathf.Max(1, p.count);
            float spacing = p.spacing > 0f ? p.spacing : 4f;
            for (int i = 0; i < count; i++)
            {
                float at = p.at + i * spacing / (CityLayout.BlockSize - 2f);
                if (!RoadAddress.TryResolve(p.street, p.between[0], p.between[1], p.direction, lane, at, out var spot, out error))
                    return false;
                var go = Spawn(kind, spot.Position, spot.Direction, i);
                spawned?.Add(go);
            }
            error = null;
            return true;
        }

        public static void ClearAll()
        {
            var root = GameObject.Find(RootName);
            if (root == null) return;
            root.name = RootName + " (cleared)"; // Destroy is deferred; don't let Root() find it again this frame
            if (Application.isPlaying) Object.Destroy(root);
            else Object.DestroyImmediate(root);
        }

        static Transform Root()
        {
            var root = GameObject.Find(RootName);
            return (root != null ? root : new GameObject(RootName)).transform;
        }
    }
}
