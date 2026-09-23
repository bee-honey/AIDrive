using System;
using UnityEngine;

namespace AIDrive.Vehicle
{
    [Serializable]
    public class SensorRay
    {
        public string name;
        [Tooltip("Ray origin in car-local space (y is overridden by the sensor height)")]
        public Vector3 origin;
        [Tooltip("Degrees from straight ahead, positive = right")]
        public float angle;
        public float range;

        public SensorRay(string name, Vector3 origin, float angle, float range)
        {
            this.name = name;
            this.origin = origin;
            this.angle = angle;
            this.range = range;
        }
    }

    public struct SensorReading
    {
        public string Name;
        public float Angle;
        public float Range;
        public bool Hit;
        /// <summary>Distance to the hit, or <see cref="Range"/> when nothing was hit.</summary>
        public float Distance;
        public Vector3 Point;
        public Collider Collider;
    }

    /// <summary>
    /// A fan of raycasts around the car, roughly what cheap ultrasonic / LiDAR sensors give a robot car.
    /// Rays sit above the 0.2 m curb so only real obstacles register. Call <see cref="Scan"/> each physics step.
    /// </summary>
    public class RaycastSensors : MonoBehaviour
    {
        public const string ProbeLeft = "probe_left";
        public const string ProbeRight = "probe_right";

        public float height = 0.5f;
        public bool showRays = true;
        /// <summary>Defined in code (not serialized) so prefab instances never drift from <see cref="DefaultRays"/>.</summary>
        [NonSerialized] public SensorRay[] rays = DefaultRays();

        public SensorReading[] Readings { get; private set; } = Array.Empty<SensorReading>();

        readonly RaycastHit[] buffer = new RaycastHit[8];
        LineRenderer[] lines;

        public static SensorRay[] DefaultRays()
        {
            var front = new Vector3(0f, 0f, 2.15f);
            return new[]
            {
                new SensorRay("front", front, 0f, 35f),
                new SensorRay("front_r15", front, 15f, 20f),
                new SensorRay("front_l15", front, -15f, 20f),
                new SensorRay("front_r35", front, 35f, 12f),
                new SensorRay("front_l35", front, -35f, 12f),
                new SensorRay("front_r60", front, 60f, 8f),
                new SensorRay("front_l60", front, -60f, 8f),
                new SensorRay("right", new Vector3(0.97f, 0f, 0f), 90f, 8f),
                new SensorRay("left", new Vector3(-0.97f, 0f, 0f), -90f, 8f),
                new SensorRay("rear", new Vector3(0f, 0f, -2.15f), 180f, 10f),
                // Parallel probes one lane-width to each side: is the neighbouring lane clear ahead?
                // Long enough to confirm the lane is clear past an obstacle (obstacle distance + passing room).
                new SensorRay(ProbeRight, new Vector3(2.6f, 0f, 2.15f), 0f, 45f),
                new SensorRay(ProbeLeft, new Vector3(-2.6f, 0f, 2.15f), 0f, 45f),
            };
        }

        public bool TryGet(string rayName, out SensorReading reading)
        {
            foreach (var r in Readings)
                if (r.Name == rayName) { reading = r; return true; }
            reading = default;
            return false;
        }

        public void Scan()
        {
            if (Readings.Length != rays.Length) Readings = new SensorReading[rays.Length];
            for (int i = 0; i < rays.Length; i++)
            {
                var ray = rays[i];
                var origin = transform.TransformPoint(new Vector3(ray.origin.x, height, ray.origin.z));
                var dir = transform.rotation * Quaternion.Euler(0f, ray.angle, 0f) * Vector3.forward;
                var reading = new SensorReading { Name = ray.name, Angle = ray.angle, Range = ray.range, Distance = ray.range, Point = origin + dir * ray.range };

                int n = Physics.RaycastNonAlloc(origin, dir, buffer, ray.range, ~0, QueryTriggerInteraction.Ignore);
                for (int h = 0; h < n; h++)
                {
                    var hit = buffer[h];
                    if (hit.collider.transform.IsChildOf(transform) || hit.distance >= reading.Distance) continue;
                    reading.Hit = true;
                    reading.Distance = hit.distance;
                    reading.Point = hit.point;
                    reading.Collider = hit.collider;
                }
                Readings[i] = reading;
            }
        }

        void Start()
        {
            lines = new LineRenderer[rays.Length];
            var mat = new Material(Shader.Find("Sprites/Default"));
            for (int i = 0; i < rays.Length; i++)
            {
                var go = new GameObject("Ray_" + rays[i].name);
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = mat;
                lr.positionCount = 2;
                lr.widthMultiplier = 0.06f;
                lr.useWorldSpace = true;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lines[i] = lr;
            }
        }

        void LateUpdate()
        {
            if (lines == null) return;
            for (int i = 0; i < lines.Length && i < Readings.Length; i++)
            {
                var lr = lines[i];
                lr.enabled = showRays;
                if (!showRays) continue;
                var r = Readings[i];
                var ray = rays[i];
                lr.SetPosition(0, transform.TransformPoint(new Vector3(ray.origin.x, height, ray.origin.z)));
                lr.SetPosition(1, r.Point);
                var c = r.Hit ? Color.Lerp(Color.red, Color.yellow, r.Distance / r.Range) : new Color(0.2f, 1f, 0.3f, 0.6f);
                lr.startColor = lr.endColor = c;
            }
        }
    }
}
