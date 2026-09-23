using System;
using UnityEngine;

namespace AIDrive.Vehicle
{
    /// <summary>
    /// Kinematic bicycle model on a Rigidbody. Takes steer / throttle / brake like a real car,
    /// so the autopilot, an agent, or a physical car can all drive through the same inputs.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class VehicleController : MonoBehaviour
    {
        [Header("Geometry")]
        public float wheelbase = 2.6f;
        public float wheelRadius = 0.36f;
        public Transform[] frontWheelPivots;
        public Transform[] wheelMeshes;

        [Header("Limits")]
        public float maxSteerAngle = 35f;
        [Tooltip("Degrees per second the front wheels can turn")]
        public float steerRate = 90f;
        public float maxSpeed = 12f;
        public float maxReverseSpeed = 3f;
        public float maxAccel = 3f;
        public float maxBrake = 7f;
        public float rollingDecel = 0.3f;

        /// <summary>Signed forward speed in m/s (negative when reversing).</summary>
        public float Speed { get; private set; }
        public float SteerAngle { get; private set; }
        public bool Reverse { get; set; }
        public int Collisions { get; private set; }
        public string LastCollision { get; private set; }
        public float DistanceTravelled { get; private set; }

        public event Action<Collision> Collided;

        float steerInput, throttleInput, brakeInput, wheelSpin;
        Rigidbody rb;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }

        /// <param name="steer">-1 (full left) … 1 (full right)</param>
        /// <param name="throttle">0 … 1</param>
        /// <param name="brake">0 … 1</param>
        public void SetControls(float steer, float throttle, float brake)
        {
            steerInput = Mathf.Clamp(steer, -1f, 1f);
            throttleInput = Mathf.Clamp01(throttle);
            brakeInput = Mathf.Clamp01(brake);
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            Speed = 0f;
            SteerAngle = 0f;
            SetControls(0f, 0f, 1f);
            transform.SetPositionAndRotation(position, rotation);
            if (rb == null) rb = GetComponent<Rigidbody>();
            rb.position = position;
            rb.rotation = rotation;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        public void ResetStats()
        {
            Collisions = 0;
            LastCollision = null;
            DistanceTravelled = 0f;
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            SteerAngle = Mathf.MoveTowards(SteerAngle, steerInput * maxSteerAngle, steerRate * dt);

            float dir = Reverse ? -1f : 1f;
            Speed += throttleInput * maxAccel * dir * dt;
            if (brakeInput > 0f) Speed = Mathf.MoveTowards(Speed, 0f, brakeInput * maxBrake * dt);
            if (throttleInput <= 0f) Speed = Mathf.MoveTowards(Speed, 0f, rollingDecel * dt);
            Speed = Mathf.Clamp(Speed, -maxReverseSpeed, maxSpeed);

            float yawRate = Speed / wheelbase * Mathf.Tan(SteerAngle * Mathf.Deg2Rad);
            rb.angularVelocity = new Vector3(0f, yawRate, 0f);
            rb.linearVelocity = rb.rotation * Vector3.forward * Speed;
            DistanceTravelled += Mathf.Abs(Speed) * dt;
        }

        void Update()
        {
            wheelSpin = (wheelSpin + Speed / wheelRadius * Mathf.Rad2Deg * Time.deltaTime) % 360f;
            var spin = Quaternion.Euler(wheelSpin, 0f, 0f) * Quaternion.Euler(0f, 0f, 90f);
            if (wheelMeshes != null)
                foreach (var w in wheelMeshes) if (w) w.localRotation = spin;
            if (frontWheelPivots != null)
                foreach (var p in frontWheelPivots) if (p) p.localRotation = Quaternion.Euler(0f, SteerAngle, 0f);
        }

        void OnCollisionEnter(Collision c)
        {
            Collisions++;
            LastCollision = c.collider.name;
            Speed = 0f;
            Collided?.Invoke(c);
        }
    }
}
