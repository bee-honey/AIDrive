using UnityEngine;

namespace AIDrive.Vehicle
{
    /// <summary>Switches the camera between a fixed city overview and a smooth chase view behind the car.</summary>
    public class CameraRig : MonoBehaviour
    {
        public enum Mode { Overview, Chase }

        public Mode mode = Mode.Chase;
        public Transform target;
        public Vector3 chaseOffset = new Vector3(0f, 4.5f, -10f);
        public float followSharpness = 4f;

        Vector3 overviewPos;
        Quaternion overviewRot;

        void Awake()
        {
            overviewPos = transform.position;
            overviewRot = transform.rotation;
        }

        public void Toggle() => mode = mode == Mode.Chase ? Mode.Overview : Mode.Chase;

        void LateUpdate()
        {
            if (mode == Mode.Overview || target == null)
            {
                transform.SetPositionAndRotation(overviewPos, overviewRot);
                return;
            }

            var yaw = Quaternion.Euler(0f, target.eulerAngles.y, 0f);
            var desired = target.position + yaw * chaseOffset;
            float k = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, desired, k);
            transform.LookAt(target.position + Vector3.up * 1.5f + yaw * Vector3.forward * 4f);
        }
    }
}
