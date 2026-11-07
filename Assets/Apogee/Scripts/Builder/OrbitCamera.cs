using UnityEngine;

namespace Apogee.Builder
{
    /// <summary>Smooth orbit/zoom/pan camera for inspecting the vehicle in the assembly building.</summary>
    public class OrbitCamera : MonoBehaviour
    {
        public Camera Cam { get; private set; }
        public float sensitivity = 1f;

        Vector3 target = new Vector3(0f, 8f, 0f), targetGoal;
        float yaw = 35f, pitch = 12f, distance = 30f;
        float yawGoal, pitchGoal, distanceGoal;

        public const float MinDistance = 3f, MaxDistance = 320f;

        void Awake()
        {
            Cam = gameObject.AddComponent<Camera>();
            Cam.nearClipPlane = 0.1f;
            Cam.farClipPlane = 2500f;
            Cam.fieldOfView = 50f;
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = new Color(0.12f, 0.13f, 0.15f);
            gameObject.tag = "MainCamera";
            gameObject.AddComponent<AudioListener>();
            targetGoal = target;
            yawGoal = yaw; pitchGoal = pitch; distanceGoal = distance;
        }

        public void Rotate(Vector2 pixelDelta)
        {
            float k = 0.18f * sensitivity * 1080f / Mathf.Max(480f, Screen.height);
            yawGoal += pixelDelta.x * k;
            pitchGoal = Mathf.Clamp(pitchGoal - pixelDelta.y * k, -8f, 82f);
        }

        public void Zoom(float factor)
        {
            distanceGoal = Mathf.Clamp(distanceGoal * factor, MinDistance, MaxDistance);
        }

        public void Pan(Vector2 pixelDelta)
        {
            float k = distanceGoal * 0.0016f * 1080f / Mathf.Max(480f, Screen.height);
            var right = transform.right;
            right.y = 0f;
            targetGoal += -right.normalized * pixelDelta.x * k + Vector3.down * pixelDelta.y * k;
            targetGoal.y = Mathf.Clamp(targetGoal.y, 0.5f, 140f);
            targetGoal.x = Mathf.Clamp(targetGoal.x, -40f, 40f);
            targetGoal.z = Mathf.Clamp(targetGoal.z, -40f, 40f);
        }

        /// <summary>Frames a world-space bounds (the rocket) nicely.</summary>
        public void Frame(Bounds b, bool instant = false)
        {
            targetGoal = b.center;
            float size = Mathf.Max(b.size.y, Mathf.Max(b.size.x, b.size.z) * 1.6f, 4f);
            distanceGoal = Mathf.Clamp(size / (2f * Mathf.Tan(Cam.fieldOfView * 0.5f * Mathf.Deg2Rad)) * 1.35f, MinDistance, MaxDistance);
            if (instant) { target = targetGoal; distance = distanceGoal; Apply(); }
        }

        /// <summary>Keeps the orbit pivot on the rocket's axis while the user builds.</summary>
        public void Follow(Bounds b)
        {
            targetGoal.y = Mathf.Clamp(targetGoal.y, b.min.y, Mathf.Max(b.min.y + 1f, b.max.y));
        }

        void LateUpdate()
        {
            float k = 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime);
            yaw = Mathf.LerpAngle(yaw, yawGoal, k);
            pitch = Mathf.Lerp(pitch, pitchGoal, k);
            distance = Mathf.Lerp(distance, distanceGoal, k);
            target = Vector3.Lerp(target, targetGoal, k);
            Apply();
        }

        void Apply()
        {
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            var pos = target - rot * Vector3.forward * distance;
            if (pos.y < 0.4f) pos.y = 0.4f;
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(target - pos, Vector3.up));
        }
    }
}
