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
        internal static bool TouchMoveForward;
        internal static bool TouchMarkDown;   // one-shot, cleared after one frame
        internal static bool TouchKickHeld;
        internal static bool TouchKickUp;     // one-shot
        internal static bool TouchHandball;   // one-shot
        internal static bool TouchTackle;     // one-shot

        // Real design change (2026-08-10, direct real-device report): a
        // free-aim D-pad "just doesn't work for this game" — matches the
        // same real-device finding that killed the earlier analog joystick
        // ("too hard for a kid to aim precisely"). Movement is a single
        // hold-to-run button again: each player always advances straight
        // down their own fixed attack lane (AFLPlayer.attackDir), no
        // steering at all. AFLPlayer reads this directly rather than
        // through a Move vector now.
        public static bool MoveForwardHeld => Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) || TouchMoveForward;
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
