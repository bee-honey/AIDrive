using System;
using System.Collections.Generic;
using System.Linq;
using AIDrive.Navigation;
using UnityEngine;

namespace AIDrive.Scenario
{
    /// <summary>Loads scenario JSON files from Resources/Scenarios.</summary>
    public static class ScenarioLibrary
    {
        public const string ResourceFolder = "Scenarios";

        public static List<ScenarioDef> LoadAll() =>
            Resources.LoadAll<TextAsset>(ResourceFolder)
                .Select(Parse)
                .OrderBy(s => s.name)
                .ToList();

        public static ScenarioDef Load(string name) =>
            LoadAll().FirstOrDefault(s => string.Equals(s.name, name, StringComparison.OrdinalIgnoreCase));

        public static ScenarioDef Parse(TextAsset asset)
        {
            var def = JsonUtility.FromJson<ScenarioDef>(asset.text);
            if (string.IsNullOrEmpty(def.name)) def.name = asset.name;
            def.props ??= Array.Empty<PropPlacement>();
            return def;
        }

        /// <summary>Static checks: landmarks exist, every prop address resolves. Empty list = valid.</summary>
        public static List<string> Validate(ScenarioDef def, RoadGraph graph)
        {
            var errors = new List<string>();
            if (!graph.TryGetLandmark(def.start, out _)) errors.Add($"Unknown start landmark '{def.start}'");
            if (!graph.TryGetLandmark(def.destination, out _)) errors.Add($"Unknown destination '{def.destination}'");
            if (def.Expect != "Arrived" && def.Expect != "Blocked") errors.Add($"expect must be Arrived or Blocked, got '{def.expect}'");

            foreach (var p in def.props)
            {
                if (!Enum.TryParse(p.type, true, out ObstacleKind _)) { errors.Add($"Unknown prop type '{p.type}'"); continue; }
                if (!RoadAddress.TryParseLane(p.lane, out var lane)) { errors.Add($"Unknown lane '{p.lane}'"); continue; }
                if (p.between == null || p.between.Length != 2) { errors.Add($"{p.type}: 'between' needs two streets"); continue; }
                if (!RoadAddress.TryResolve(p.street, p.between[0], p.between[1], p.direction, lane, p.at, out _, out var err))
                    errors.Add($"{p.type}: {err}");
            }
            return errors;
        }
    }
}
