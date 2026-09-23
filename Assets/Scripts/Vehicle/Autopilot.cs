using System;
using System.Collections.Generic;
using AIDrive.Navigation;
using AIDrive.Traffic;
using UnityEngine;

namespace AIDrive.Vehicle
{
    /// <summary>
    /// Low-level driver: plans a route from the car's current pose, converts it to a lane path,
    /// and follows it with Stanley steering (plus curvature feed-forward) and a curvature-aware speed profile.
    /// Raycast hits are projected onto the path so only obstacles in the driving corridor matter: the car
    /// brakes for them, changes to the other same-direction lane to pass, or reports Blocked.
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    public class Autopilot : MonoBehaviour
    {
        public enum State { Idle, Driving, StoppedAtLight, Waiting, Blocked, Arrived, Failed }

        [Header("Speed")]
        public float cruiseSpeed = 11f;
        public float minCornerSpeed = 4f;
        [Tooltip("Max sideways acceleration in turns, m/s²")]
        public float maxLateralAccel = 2.5f;
        public float comfortDecel = 3f;

        [Header("Steering (Stanley)")]
        [Tooltip("How hard to correct sideways error at the front axle")]
        public float stanleyGain = 2f;
        [Tooltip("Keeps the correction sane at very low speed")]
        public float stanleySoftening = 1f;
        [Tooltip("Seconds ahead to read path curvature, compensating for steering lag")]
        public float feedForwardTime = 0.3f;

        [Header("Arrival")]
        public float arriveTolerance = 1.5f;

        [Header("Obstacles")]
        [Tooltip("Gap to keep between the front bumper and an obstacle when stopped")]
        public float stopGap = 3f;
        public float hardDecel = 5f;
        [Tooltip("Half the car width plus margin: obstacles closer than this to a lane centre block that lane")]
        public float corridorHalfWidth = 1.45f;
        [Tooltip("Offset from our lane to the neighbouring same-direction lane")]
        public float otherLaneShift = 2.6f;
        [Tooltip("React to in-lane obstacles closer than this")]
        public float avoidLookahead = 30f;
        public float laneChangeSpeed = 7f;
        [Tooltip("Seconds stopped behind an obstacle before declaring Blocked")]
        public float blockedAfter = 2f;

        [Header("Traffic signals")]
        [Tooltip("Stop for a yellow only if it needs no more than this deceleration (m/s²); otherwise go through")]
        public float yellowStopDecel = 4f;
        [Tooltip("Start reacting to a signal this far before its stop line")]
        public float signalLookahead = 60f;

        const float FrontOffset = 2.1f;

        public State CurrentState { get; private set; } = State.Idle;
        public string Destination { get; private set; }
        public Route Route { get; private set; }
        public LanePath Path { get; private set; }
        public string LastError { get; private set; }
        public float DistanceRemaining { get; private set; }
        public float CrossTrackError { get; private set; }
        public float MaxCrossTrackError { get; private set; }
        /// <summary>Distance along the path where <see cref="MaxCrossTrackError"/> occurred.</summary>
        public float MaxCrossTrackAt { get; private set; }
        public float TripTime { get; private set; }
        /// <summary>Current sideways offset from the planned lane (0 = own lane, otherLaneShift = passing lane).</summary>
        public float LaneShift { get; private set; }
        public int LaneChanges { get; private set; }
        /// <summary>Along-path distance from the front bumper to the nearest obstacle in the car's corridor.</summary>
        public float PathObstacleDistance { get; private set; } = float.PositiveInfinity;
        public string PathObstacleName { get; private set; }
        public BlockInfo Blocked { get; private set; }
        public int RedLightStops { get; private set; }
        public int RedLightViolations { get; private set; }
        public float TimeAtLights { get; private set; }
        /// <summary>e.g. "Red at S3 & E2 in 35 m"; null when no signal ahead.</summary>
        public string NextSignal { get; private set; }

        public event Action<Autopilot> Arrived;
        public event Action<Autopilot, BlockInfo> BlockedDetected;

        public VehicleController Vehicle => car;
        public RaycastSensors Sensors => sensors;

        struct PathHit
        {
            public float Ahead;
            public float Lateral;
            public Vector3 Point;
            public string Name;
        }

        VehicleController car;
        RaycastSensors sensors;
        float[] speedProfile;
        int progress;
        float targetShift;
        float passUntil;
        float stoppedFor;
        int nextStop;
        bool goingThroughYellow;
        float lightStop = float.PositiveInfinity;
        readonly List<PathHit> hits = new List<PathHit>();

        void Awake()
        {
            car = GetComponent<VehicleController>();
            sensors = GetComponent<RaycastSensors>();
        }

        public VehicleLocation Locate() => Localizer.Locate(CityMap.Instance.Graph, transform.position, transform.forward);

        public VehicleState CaptureState() => VehicleState.Capture(this);

        public bool DriveTo(string landmark, RouteOptions options = null)
        {
            var graph = CityMap.Instance.Graph;
            if (graph.TryGetLandmark(landmark, out var goal) && Vector3.Distance(Flat(transform.position), goal.Position) < 6f)
            {
                Destination = goal.Name;
                SetArrived();
                return true;
            }

            var route = RoutePlanner.PlanFrom(graph, transform.position, transform.forward, landmark, options, out var loc);
            if (!route.Success)
            {
                LastError = route.Error;
                CurrentState = State.Failed;
                return false;
            }

            var startDir = (graph.Nodes[loc.NextNode].Position - graph.Nodes[loc.FromNode].Position).normalized;
            Route = route;
            Destination = graph.Nodes[route.NodeIds[route.NodeIds.Count - 1]].Name;
            Path = LanePath.Build(graph, route, transform.position, startDir, LaneSettings.Default);
            speedProfile = BuildSpeedProfile(Path);
            progress = 0;
            MaxCrossTrackError = 0f;
            TripTime = 0f;
            LaneShift = targetShift = 0f;
            LaneChanges = 0;
            stoppedFor = 0f;
            nextStop = 0;
            goingThroughYellow = false;
            RedLightStops = RedLightViolations = 0;
            TimeAtLights = 0f;
            NextSignal = null;
            Blocked = null;
            LastError = null;
            car.Reverse = false;
            CurrentState = State.Driving;
            Debug.Log($"Autopilot: driving to {Destination} via {route.Summary} from {loc}");
            return true;
        }

        public void Stop()
        {
            CurrentState = State.Idle;
            car.SetControls(0f, 0f, 1f);
        }

        /// <summary>The next upcoming maneuver, e.g. "In 45 m: Turn left onto S6".</summary>
        public string NextInstruction
        {
            get
            {
                if (!IsActive || Path == null) return null;
                float s = Path.Distances[progress];
                foreach (var m in Path.Maneuvers)
                    if (m.Distance > s - 1f) return $"In {Mathf.Max(0f, m.Distance - s):0} m: {m.Text}";
                return null;
            }
        }

        /// <summary>Following a route: driving, or stopped for an obstacle.</summary>
        public bool IsActive => CurrentState == State.Driving || CurrentState == State.StoppedAtLight ||
                                CurrentState == State.Waiting || CurrentState == State.Blocked;

        void FixedUpdate()
        {
            if (sensors != null) sensors.Scan();
            if (!IsActive)
            {
                car.SetControls(0f, 0f, 1f);
                return;
            }

            float dt = Time.fixedDeltaTime;
            TripTime += dt;
            var pos = Flat(transform.position);
            var fwd = Flat(transform.forward).normalized;
            float v = car.Speed;

            progress = Path.ClosestIndex(pos, progress, 25);
            float s = Path.Distances[progress];
            var tangentHere = Tangent(progress);
            float carLateral = Vector3.Dot(pos - Path.Points[progress], LanePath.RightOf(tangentHere));
            CrossTrackError = Mathf.Abs(carLateral - LaneShift);
            if (CrossTrackError > MaxCrossTrackError)
            {
                MaxCrossTrackError = CrossTrackError;
                MaxCrossTrackAt = s;
            }
            DistanceRemaining = Path.Length - s - Vector3.Dot(pos - Path.Points[progress], tangentHere);

            if (DistanceRemaining < arriveTolerance && Mathf.Abs(v) < 0.3f)
            {
                SetArrived();
                return;
            }

            // --- Traffic signals: must we stop at the next stop line?
            lightStop = CheckSignals(s + FrontOffset, v);

            // --- Perception: which sensor hits lie in which lane ahead of us?
            Perceive(s);
            UpdateLaneChoice(s, v, dt);
            float here = HitsInLane(carLateral, out var hereName);
            float there = HitsInLane(targetShift, out var thereName);
            float obstacle = Mathf.Min(here, there);
            PathObstacleDistance = obstacle;
            PathObstacleName = float.IsInfinity(obstacle) ? null : here <= there ? hereName : thereName;

            // --- Steering: Stanley toward the (possibly shifted) lane, plus curvature feed-forward.
            var front = pos + fwd * (car.wheelbase / 2f);
            int fi = Path.ClosestIndex(front, progress, 30);
            var tangent = Tangent(fi);
            float headingErr = Vector3.SignedAngle(fwd, tangent, Vector3.up) * Mathf.Deg2Rad;
            float lateral = Vector3.Dot(front - Path.Points[fi], LanePath.RightOf(tangent)) - LaneShift; // + = right of target
            float correction = Mathf.Atan2(-stanleyGain * lateral, stanleySoftening + Mathf.Abs(v));
            int ffi = Mathf.Min(Path.Points.Count - 1, fi + Mathf.RoundToInt(Mathf.Abs(v) * feedForwardTime / LaneSettings.Default.Spacing));
            float feedForward = Mathf.Atan(car.wheelbase * Curvature(ffi));
            float steer = (headingErr + correction + feedForward) * Mathf.Rad2Deg / car.maxSteerAngle;

            // --- Speed: profile, stop at the end, stop before obstacles, slow while changing lanes.
            float targetSpeed = speedProfile[Mathf.Min(progress + 1, speedProfile.Length - 1)];
            targetSpeed = Mathf.Min(targetSpeed, Mathf.Sqrt(2f * comfortDecel * Mathf.Max(0f, DistanceRemaining - 0.3f)));
            if (!float.IsInfinity(obstacle))
                targetSpeed = Mathf.Min(targetSpeed, Mathf.Sqrt(2f * hardDecel * Mathf.Max(0f, obstacle - stopGap)));
            if (!float.IsInfinity(lightStop))
                targetSpeed = Mathf.Min(targetSpeed, Mathf.Sqrt(2f * comfortDecel * Mathf.Max(0f, lightStop - 0.3f)));
            if (Mathf.Abs(LaneShift - targetShift) > 0.05f)
                targetSpeed = Mathf.Min(targetSpeed, laneChangeSpeed);

            float err = targetSpeed - v;
            float throttle = err > 0f ? Mathf.Clamp01(err * 0.6f) : 0f;
            float brake = err < -0.2f ? Mathf.Clamp01(-err * 0.5f) : 0f;
            // Brake by the deceleration actually needed to reach the nearest stop point, instead of waiting for speed error.
            float room = Mathf.Min(obstacle - stopGap, lightStop - 0.3f);
            if (!float.IsInfinity(room))
            {
                float needed = room > 0.1f ? v * v / (2f * room) : float.PositiveInfinity;
                if (needed > 1f)
                {
                    throttle = 0f;
                    brake = Mathf.Max(brake, Mathf.Clamp01(needed / car.maxBrake));
                }
            }
            if (DistanceRemaining < 0.4f || obstacle < stopGap * 0.7f || lightStop < 0.2f) { throttle = 0f; brake = 1f; }
            car.SetControls(steer, throttle, brake);

            UpdateStoppedState(obstacle, v, dt);
        }

        void Perceive(float s)
        {
            hits.Clear();
            if (sensors == null) return;
            float halfLength = 2.1f;
            foreach (var r in sensors.Readings)
            {
                if (!r.Hit || Mathf.Abs(r.Angle) > 90f) continue; // ignore the rear ray
                if (!Path.Project(r.Point, progress, 80, out float hs, out float hl)) continue;
                float ahead = hs - s - halfLength;
                if (ahead < -1f) continue;
                hits.Add(new PathHit { Ahead = ahead, Lateral = hl, Point = r.Point, Name = Scenario.Obstacle.Describe(r.Collider) });
            }
        }

        /// <summary>Nearest hit ahead whose lateral offset falls within the corridor around <paramref name="laneCentre"/>.</summary>
        float HitsInLane(float laneCentre) => HitsInLane(laneCentre, out _);

        float HitsInLane(float laneCentre, out string name)
        {
            float nearest = float.PositiveInfinity;
            name = null;
            foreach (var h in hits)
                if (Mathf.Abs(h.Lateral - laneCentre) < corridorHalfWidth && h.Ahead < nearest)
                {
                    nearest = h.Ahead;
                    name = h.Name;
                }
            return nearest;
        }

        void UpdateLaneChoice(float s, float v, float dt)
        {
            if (targetShift == 0f)
            {
                float mine = HitsInLane(0f);
                if (mine < avoidLookahead && StraightAhead(s, mine + 15f))
                {
                    // "No hit" only means clear as far as the lane probe can see.
                    float other = Mathf.Min(HitsInLane(otherLaneShift), ProbeRange(otherLaneShift));
                    if (other > mine + 12f)
                    {
                        targetShift = otherLaneShift;
                        passUntil = s + mine + 10f;
                        LaneChanges++;
                        Debug.Log($"Autopilot: changing lane at s={s:0} — own lane blocked by {PathObstacleName ?? "obstacle"} " +
                                  $"in {mine:0.0} m, other lane clear for {other:0.0} m");
                    }
                }
            }
            else
            {
                bool mustReturn = !StraightAhead(s, 15f);
                bool passed = s > passUntil && Mathf.Min(HitsInLane(0f), ProbeRange(0f)) > 15f;
                if (mustReturn || passed) targetShift = 0f;
            }

            float rate = Mathf.Max(0.8f, Mathf.Abs(v) * otherLaneShift / 12f); // full lane change over ~12 m
            LaneShift = Mathf.MoveTowards(LaneShift, targetShift, rate * dt);
        }

        /// <summary>How far ahead the lane probe on the side of <paramref name="laneCentre"/> can see.</summary>
        float ProbeRange(float laneCentre)
        {
            if (sensors == null) return 0f;
            string probe = laneCentre > LaneShift ? RaycastSensors.ProbeRight : RaycastSensors.ProbeLeft;
            return sensors.TryGet(probe, out var r) ? r.Range : 0f;
        }

        /// <summary>True if [s, s+distance] avoids turn curves, the start merge and the final pull-over.</summary>
        bool StraightAhead(float s, float distance)
        {
            float end = s + distance;
            if (s < 15f || end > Path.Length - 30f) return false;
            for (int i = 0; i < Path.Maneuvers.Count - 1; i++) // last maneuver is the arrival
            {
                float m = Path.Maneuvers[i].Distance;
                if (end > m - 12f && s < m + 26f) return false;
            }
            return true;
        }

        void UpdateStoppedState(float obstacle, float v, float dt)
        {
            bool atLight = !float.IsInfinity(lightStop) && lightStop < 3f && Mathf.Abs(v) < 0.3f;
            if (!float.IsInfinity(lightStop) && Mathf.Abs(v) < 0.5f) TimeAtLights += dt;

            bool heldUp = obstacle < stopGap + 2f && Mathf.Abs(v) < 0.3f;
            if (!heldUp)
            {
                stoppedFor = 0f;
                if (atLight)
                {
                    if (CurrentState != State.StoppedAtLight) RedLightStops++;
                    CurrentState = State.StoppedAtLight;
                    return;
                }
                if (CurrentState != State.Driving)
                {
                    if (CurrentState == State.Waiting || CurrentState == State.Blocked)
                        Debug.Log($"Autopilot: path clear, resuming to {Destination}");
                    CurrentState = State.Driving;
                    Blocked = null;
                }
                return;
            }

            stoppedFor += dt;
            if (stoppedFor < blockedAfter)
            {
                CurrentState = State.Waiting;
                return;
            }
            if (CurrentState == State.Blocked) return;

            var hit = hits.Find(h => Mathf.Approximately(h.Ahead, obstacle));
            var loc = Localizer.Locate(CityMap.Instance.Graph, hit.Point, transform.forward);
            Blocked = new BlockInfo
            {
                street = loc.Street,
                between = loc.Between,
                location = loc.Description,
                distance_m = VehicleState.Round(obstacle),
                by = PathObstacleName,
                x = VehicleState.Round(hit.Point.x),
                z = VehicleState.Round(hit.Point.z),
            };
            CurrentState = State.Blocked;
            Debug.Log($"Autopilot: BLOCKED on {Blocked.street} between {Blocked.between} by {Blocked.by} ({Blocked.distance_m} m ahead)");
            BlockedDetected?.Invoke(this, Blocked);
        }

        /// <summary>
        /// Distance from the front bumper to the stop line the car must stop at, or +∞ if it may proceed.
        /// Stops on red; stops on yellow only when that is comfortably possible (otherwise commits to going through).
        /// Counts a violation whenever the front bumper crosses a stop line on red.
        /// </summary>
        float CheckSignals(float front, float v)
        {
            var stops = Path.StopPoints;
            while (nextStop < stops.Count && stops[nextStop].Distance < front)
            {
                var passed = stops[nextStop];
                var passedSignal = SignalAt(passed.NodeId);
                if (passedSignal != null && passedSignal.StateFor(SignalTiming.AxisOf(passed.Direction)) == SignalState.Red)
                {
                    RedLightViolations++;
                    Debug.LogWarning($"Autopilot: RED LIGHT VIOLATION at {passedSignal.Name}");
                }
                nextStop++;
                goingThroughYellow = false;
            }

            NextSignal = null;
            for (int i = nextStop; i < stops.Count; i++)
            {
                float remain = stops[i].Distance - front;
                if (remain > signalLookahead) break;
                var signal = SignalAt(stops[i].NodeId);
                if (signal == null) continue; // unsignalised intersection

                var state = signal.StateFor(SignalTiming.AxisOf(stops[i].Direction));
                NextSignal = $"{state} at {signal.Name} in {Mathf.Max(0f, remain):0} m";

                // Only the first signalised stop line ahead matters right now.
                if (state == SignalState.Green) { goingThroughYellow = false; return float.PositiveInfinity; }
                if (state == SignalState.Yellow)
                {
                    bool canStop = remain > 0.5f && v * v / (2f * remain) <= yellowStopDecel;
                    if (goingThroughYellow || !canStop) { goingThroughYellow = true; return float.PositiveInfinity; }
                }
                return remain;
            }
            return float.PositiveInfinity;
        }

        TrafficSignal SignalAt(int nodeId)
        {
            var node = CityMap.Instance.Graph.Nodes[nodeId];
            return node.Kind == NodeKind.Intersection ? TrafficSignal.At(node.GridX, node.GridZ) : null;
        }

        void SetArrived()
        {
            CurrentState = State.Arrived;
            DistanceRemaining = 0f;
            car.SetControls(0f, 0f, 1f);
            Arrived?.Invoke(this);
        }

        /// <summary>Per-point speed limit from path curvature, then a backward pass so the car can always brake in time.</summary>
        float[] BuildSpeedProfile(LanePath path)
        {
            int n = path.Points.Count;
            var profile = new float[n];
            const int w = 3;
            for (int i = 0; i < n; i++)
            {
                profile[i] = cruiseSpeed;
                if (i < w || i >= n - w) continue;
                var d1 = path.Points[i] - path.Points[i - w];
                var d2 = path.Points[i + w] - path.Points[i];
                float angle = Vector3.Angle(d1, d2) * Mathf.Deg2Rad;
                float arc = (d1.magnitude + d2.magnitude) / 2f;
                if (angle < 1e-3f || arc < 1e-3f) continue;
                float radius = arc / angle;
                profile[i] = Mathf.Clamp(Mathf.Sqrt(maxLateralAccel * radius), minCornerSpeed, cruiseSpeed);
            }
            // Stopping at the end is handled by the distance-to-go rule in FixedUpdate.
            for (int i = n - 2; i >= 0; i--)
            {
                float ds = path.Distances[i + 1] - path.Distances[i];
                profile[i] = Mathf.Min(profile[i], Mathf.Sqrt(profile[i + 1] * profile[i + 1] + 2f * comfortDecel * ds));
            }
            return profile;
        }

        /// <summary>Signed path curvature (1/m) at point i; positive = curving right.</summary>
        float Curvature(int i)
        {
            const int w = 2;
            int a = Mathf.Max(0, i - w), b = Mathf.Min(Path.Points.Count - 1, i + w);
            if (b - a < 2) return 0f;
            var t1 = Path.Points[i] - Path.Points[a];
            var t2 = Path.Points[b] - Path.Points[i];
            if (t1.sqrMagnitude < 1e-6f || t2.sqrMagnitude < 1e-6f) return 0f;
            float angle = Vector3.SignedAngle(t1, t2, Vector3.up) * Mathf.Deg2Rad;
            return angle / ((t1.magnitude + t2.magnitude) / 2f);
        }

        Vector3 Tangent(int i)
        {
            int a = Mathf.Max(0, i - 1), b = Mathf.Min(Path.Points.Count - 1, i + 1);
            var t = Path.Points[b] - Path.Points[a];
            return t.sqrMagnitude > 1e-6f ? t.normalized : Flat(transform.forward).normalized;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        void OnDrawGizmos()
        {
            if (Path == null || !IsActive) return;
            Gizmos.color = Color.cyan;
            for (int i = progress; i < Path.Points.Count - 1; i++)
                Gizmos.DrawLine(Path.Points[i] + Vector3.up * 0.2f, Path.Points[i + 1] + Vector3.up * 0.2f);
        }
    }
}
