using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LayoutRecommender.Core
{
    /// <summary>새 Input System과 기존 Input Manager 어느 쪽이든 같은 방식으로 마우스를 읽는다.</summary>
    public static class InputBridge
    {
#if ENABLE_INPUT_SYSTEM
        static Mouse M => Mouse.current;
        public static Vector2 MousePosition => M != null ? M.position.ReadValue() : Vector2.zero;
        public static Vector2 MouseDelta => M != null ? M.delta.ReadValue() : Vector2.zero;
        public static bool LeftPressed => M != null && M.leftButton.wasPressedThisFrame;
        public static bool RightHeld => M != null && M.rightButton.isPressed;
        public static bool MiddleHeld => M != null && M.middleButton.isPressed;
        public static bool ShiftHeld => Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
        public static float Scroll
        {
            get
            {
                if (M == null) return 0f;
                float y = M.scroll.ReadValue().y;
                return Mathf.Abs(y) > 10f ? y / 120f : y; // 플랫폼에 따라 120 단위로 들어오는 경우 보정
            }
        }
#else
        public static Vector2 MousePosition => Input.mousePosition;
        public static Vector2 MouseDelta => new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 20f;
        public static bool LeftPressed => Input.GetMouseButtonDown(0);
        public static bool RightHeld => Input.GetMouseButton(1);
        public static bool MiddleHeld => Input.GetMouseButton(2);
        public static bool ShiftHeld => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        public static float Scroll => Input.mouseScrollDelta.y;
#endif

        /// <summary>마우스가 UI 위에 있으면 3D 조작을 하지 않는다.</summary>
        public static bool PointerOverUI => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }
}
