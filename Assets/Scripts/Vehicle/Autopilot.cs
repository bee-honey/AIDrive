using System;
using AIDrive.Navigation;
using UnityEngine;

namespace AIDrive.Vehicle
{
    /// <summary>
    /// Low-level driver: plans a route from the car's current pose, converts it to a lane path,
    /// and follows it with Stanley steering (plus curvature feed-forward) and a curvature-aware speed profile.
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    public class Autopilot : MonoBehaviour
    {
        public enum State { Idle, Driving, Arrived, Failed }

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

        public event Action<Autopilot> Arrived;

        public VehicleController Vehicle => car;

        VehicleController car;
        float[] speedProfile;
        int progress;

        void Awake() => car = GetComponent<VehicleController>();

        public VehicleLocation Locate() => Localizer.Locate(CityMap.Instance.Graph, transform.position, transform.forward);

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
                if (CurrentState != State.Driving || Path == null) return null;
                float s = Path.Distances[progress];
                foreach (var m in Path.Maneuvers)
                    if (m.Distance > s - 1f) return $"In {Mathf.Max(0f, m.Distance - s):0} m: {m.Text}";
                return null;
            }
        }

        void FixedUpdate()
        {
            if (CurrentState != State.Driving)
            {
                car.SetControls(0f, 0f, 1f);
                return;
            }

            TripTime += Time.fixedDeltaTime;
            var pos = Flat(transform.position);
            var fwd = Flat(transform.forward).normalized;

            progress = Path.ClosestIndex(pos, progress, 25);
            CrossTrackError = CrossTrack(pos, progress);
            if (CrossTrackError > MaxCrossTrackError)
            {
                MaxCrossTrackError = CrossTrackError;
                MaxCrossTrackAt = Path.Distances[progress];
            }
            DistanceRemaining = Path.Length - Path.Distances[progress]
                                - Vector3.Dot(pos - Path.Points[progress], Tangent(progress));

            if (DistanceRemaining < arriveTolerance && Mathf.Abs(car.Speed) < 0.3f)
            {
                SetArrived();
                return;
            }

            // Stanley: heading error + cross-track correction at the front axle, plus curvature feed-forward.
            float v = car.Speed;
            var front = pos + fwd * (car.wheelbase / 2f);
            int fi = Path.ClosestIndex(front, progress, 30);
            var tangent = Tangent(fi);
            float headingErr = Vector3.SignedAngle(fwd, tangent, Vector3.up) * Mathf.Deg2Rad;
            float lateral = Vector3.Dot(front - Path.Points[fi], LanePath.RightOf(tangent)); // + = right of path
            float correction = Mathf.Atan2(-stanleyGain * lateral, stanleySoftening + Mathf.Abs(v));
            int ffi = Mathf.Min(Path.Points.Count - 1, fi + Mathf.RoundToInt(Mathf.Abs(v) * feedForwardTime / LaneSettings.Default.Spacing));
            float feedForward = Mathf.Atan(car.wheelbase * Curvature(ffi));
            float steer = (headingErr + correction + feedForward) * Mathf.Rad2Deg / car.maxSteerAngle;

            // Speed: follow the profile, and brake to stop at the end of the path.
            float targetSpeed = speedProfile[Mathf.Min(progress + 1, speedProfile.Length - 1)];
            targetSpeed = Mathf.Min(targetSpeed, Mathf.Sqrt(2f * comfortDecel * Mathf.Max(0f, DistanceRemaining - 0.3f)));

            float err = targetSpeed - v;
            float throttle = err > 0f ? Mathf.Clamp01(err * 0.6f) : 0f;
            float brake = err < -0.2f ? Mathf.Clamp01(-err * 0.5f) : 0f;
            if (DistanceRemaining < 0.4f) { throttle = 0f; brake = 1f; }
            car.SetControls(steer, throttle, brake);
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

        float CrossTrack(Vector3 pos, int i)
        {
            var t = Tangent(i);
            return Mathf.Abs(Vector3.Dot(pos - Path.Points[i], LanePath.RightOf(t)));
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        void OnDrawGizmos()
        {
            if (Path == null || CurrentState != State.Driving) return;
            Gizmos.color = Color.cyan;
            for (int i = progress; i < Path.Points.Count - 1; i++)
                Gizmos.DrawLine(Path.Points[i] + Vector3.up * 0.2f, Path.Points[i + 1] + Vector3.up * 0.2f);
        }
    }
}
