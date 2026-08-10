using UnityEngine;

namespace AFL
{
    // =======================================================================
    //  INPUT WRAPPER  — swap the bodies out if you move to the Input System
    // =======================================================================
    // Touch overlay (2026-08-10): every property below merges real
    // keyboard/mouse input with state written by AFLTouchBridge, so
    // AFLPlayer/AFLGameManager/AFLBroadcastCamera never need to know or
    // care which input source is actually driving them — this file stays
    // the single point of truth, matching its own header comment above.
    public static class AFLInput
    {
        internal static Vector2 TouchMove;
        internal static bool TouchMarkDown;   // one-shot, cleared after one frame
        internal static bool TouchKickHeld;
        internal static bool TouchKickUp;     // one-shot
        internal static bool TouchHandball;   // one-shot
        internal static bool TouchTackle;     // one-shot

        public static Vector2 Move
        {
            get
            {
                Vector2 kb = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
                Vector2 combined = kb + TouchMove;
                return combined.sqrMagnitude > 1f ? combined.normalized : combined;
            }
        }
        public static Vector2 Look   => new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
        public static bool Sprint    => Input.GetKey(KeyCode.LeftShift);
        public static bool MarkDown  => Input.GetKeyDown(KeyCode.Space) || TouchMarkDown;   // jump / mark / gather
        public static bool KickHeld  => Input.GetMouseButton(0) || TouchKickHeld;
        public static bool KickUp    => Input.GetMouseButtonUp(0) || TouchKickUp;
        public static bool Handball  => Input.GetMouseButtonDown(1) || TouchHandball;
        public static bool Tackle    => Input.GetKeyDown(KeyCode.E) || TouchTackle;
        public static bool Switch    => Input.GetKeyDown(KeyCode.Q);

        // Called once per frame, after every AFLPlayer/AFLGameManager Update
        // has had a chance to read this frame's one-shot touch flags — see
        // AFLTouchBridge.LateUpdate().
        internal static void ClearOneShotTouchFlags()
        {
            TouchMarkDown = false;
            TouchKickUp = false;
            TouchHandball = false;
            TouchTackle = false;
        }
    }

    public enum MarkGrade { Screamer, Clunk, Fumble, Spoil, Miss }
}
