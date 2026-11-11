using LayoutRecommender.Core;
using UnityEngine;

namespace LayoutRecommender.World
{
    /// <summary>
    /// 레이아웃 보기용 카메라. 오른쪽 드래그: 회전 · 가운데(또는 Shift+오른쪽) 드래그: 이동 · 휠: 확대/축소
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraRig : MonoBehaviour
    {
        public Vector3 Target;
        public float Yaw = 0f;
        public float Pitch = 50f;
        public float Distance = 120f;
        public bool InputEnabled = true;

        float _minDist = 5f, _maxDist = 600f;

        /// <summary>건물 크기에 맞춰 화면에 들어오게 한다.</summary>
        public void Frame(float width, float depth)
        {
            float size = Mathf.Max(width, depth);
            Target = new Vector3(0f, 0f, -depth * 0.05f);
            Yaw = 0f;
            Pitch = 52f;
            Distance = size * 1.05f;
            _maxDist = size * 4f;
            _minDist = 5f;
            var cam = GetComponent<Camera>();
            cam.farClipPlane = Mathf.Max(1000f, size * 6f);
            cam.nearClipPlane = 0.3f;
            Apply();
        }

        void LateUpdate()
        {
            if (InputEnabled && !InputBridge.PointerOverUI)
            {
                Vector2 d = InputBridge.MouseDelta;
                bool pan = InputBridge.MiddleHeld || (InputBridge.RightHeld && InputBridge.ShiftHeld);
                if (pan)
                {
                    float k = Distance * 0.0015f;
                    var right = transform.right; right.y = 0; right.Normalize();
                    var fwd = Vector3.Cross(right, Vector3.up);
                    Target -= right * d.x * k + fwd * d.y * k;
                }
                else if (InputBridge.RightHeld)
                {
                    Yaw += d.x * 0.25f;
                    Pitch = Mathf.Clamp(Pitch - d.y * 0.25f, 10f, 89f);
                }

                float s = InputBridge.Scroll;
                if (Mathf.Abs(s) > 0.01f)
                    Distance = Mathf.Clamp(Distance * Mathf.Pow(0.88f, s), _minDist, _maxDist);
            }
            Apply();
        }

        void Apply()
        {
            var rot = Quaternion.Euler(Pitch, Yaw, 0f);
            transform.position = Target + rot * new Vector3(0f, 0f, -Distance);
            transform.rotation = rot;
        }
    }
}
