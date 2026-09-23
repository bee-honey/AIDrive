using System;
using System.Collections;
using System.Collections.Generic;
using AIDrive.Navigation;
using AIDrive.Vehicle;
using UnityEngine;

namespace AIDrive.Scenario
{
    /// <summary>
    /// Runs a <see cref="ScenarioDef"/>: clears old props, places new ones, parks the car at the start,
    /// drives to the destination and records a <see cref="ScenarioResult"/>. The seed of the M10 eval harness.
    /// </summary>
    [RequireComponent(typeof(Autopilot))]
    public class ScenarioRunner : MonoBehaviour
    {
        public bool Running { get; private set; }
        public ScenarioDef Current { get; private set; }
        public ScenarioResult LastResult { get; private set; }

        public event Action<ScenarioResult> Finished;

        Autopilot autopilot;

        void Awake() => autopilot = GetComponent<Autopilot>();

        public void StartScenario(ScenarioDef def)
        {
            if (Running) StopAllCoroutines();
            StartCoroutine(Run(def));
        }

        public IEnumerator Run(ScenarioDef def)
        {
            Running = true;
            Current = def;
            var result = new ScenarioResult { scenario = def.name, expected = def.Expect };
            var graph = CityMap.Instance.Graph;

            autopilot.Stop();
            PropPlacer.ClearAll();
            yield return null;

            var errors = ScenarioLibrary.Validate(def, graph);
            var spawned = new List<GameObject>();
            foreach (var p in def.props)
                if (!PropPlacer.TryPlace(p, spawned, out var err)) errors.Add(err);
            if (errors.Count > 0)
            {
                Finish(result, "Invalid", string.Join("; ", errors));
                yield break;
            }

            graph.TryGetLandmark(def.start, out var start);
            var pose = LanePath.CurbPose(start, LaneSettings.Default);
            autopilot.Vehicle.Teleport(pose.position, pose.rotation);
            autopilot.Vehicle.ResetStats();
            yield return new WaitForFixedUpdate();

            if (!autopilot.DriveTo(def.destination))
            {
                Finish(result, "Failed", autopilot.LastError);
                yield break;
            }

            float started = Time.time;
            while (autopilot.IsActive && autopilot.CurrentState != Autopilot.State.Blocked && Time.time - started < def.Timeout)
                yield return null;

            string outcome = autopilot.CurrentState == Autopilot.State.Arrived ? "Arrived"
                           : autopilot.CurrentState == Autopilot.State.Blocked ? "Blocked"
                           : autopilot.IsActive ? "Timeout"
                           : autopilot.CurrentState.ToString();
            if (outcome == "Timeout") autopilot.Stop();

            result.time_s = VehicleState.Round(autopilot.TripTime);
            result.distance_m = VehicleState.Round(autopilot.Vehicle.DistanceTravelled);
            result.route_m = VehicleState.Round(autopilot.Route.Length);
            result.collisions = autopilot.Vehicle.Collisions;
            result.lane_changes = autopilot.LaneChanges;
            result.max_lane_error_m = Math.Round(autopilot.MaxCrossTrackError, 2);
            result.red_light_stops = autopilot.RedLightStops;
            result.red_light_violations = autopilot.RedLightViolations;
            result.time_at_lights_s = VehicleState.Round(autopilot.TimeAtLights);
            if (outcome == "Blocked" && autopilot.Blocked != null)
            {
                result.blocked_by = autopilot.Blocked.by;
                result.blocked_at = $"{autopilot.Blocked.street} between {autopilot.Blocked.between}";
            }

            var failures = new List<string>();
            if (outcome != def.Expect) failures.Add($"expected {def.Expect}, got {outcome}");
            if (result.collisions > 0) failures.Add($"{result.collisions} collision(s), last with {autopilot.Vehicle.LastCollision}");
            if (result.red_light_violations > 0) failures.Add($"{result.red_light_violations} red-light violation(s)");
            if (result.lane_changes < def.min_lane_changes) failures.Add($"{result.lane_changes} lane changes, expected ≥ {def.min_lane_changes}");
            Finish(result, outcome, string.Join("; ", failures));
        }

        void Finish(ScenarioResult result, string outcome, string failure)
        {
            result.outcome = outcome;
            result.failure = failure ?? "";
            result.passed = string.IsNullOrEmpty(result.failure);
            LastResult = result;
            Running = false;
            Debug.Log($"Scenario {(result.passed ? "PASSED" : "FAILED")}: {JsonUtility.ToJson(result)}");
            Finished?.Invoke(result);
        }
    }
}
