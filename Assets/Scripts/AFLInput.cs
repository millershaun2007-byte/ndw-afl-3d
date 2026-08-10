using UnityEngine;

namespace AFL
{
    // =======================================================================
    //  INPUT WRAPPER  — swap the bodies out if you move to the Input System
    // =======================================================================
    // Rebuilt 2026-08-11 per issue #1 / docs/FOOTY-REBUILD.md: three buttons
    // only — MOVE, MARK, KICK. Handball/Tackle/Switch and the free-look
    // mouse axes are gone, not just unused — Look read the mouse axes
    // unconditionally (no button gate at all), which is why the camera used
    // to swing on any stray pointer movement, and the old KickHeld read
    // Input.GetMouseButton(0) directly, which is why any tap anywhere on
    // the canvas charged/fired a kick. Neither has a replacement; removing
    // the source is the fix, not adding a guard on top of it.
    public static class AFLInput
    {
        // Public, not internal: the Editor-only automated playtest
        // (Assets/Editor/PlaytestDriver.cs) drives these directly from a
        // separate assembly, the same way the real HTML control bar does
        // via AFLTouchBridge.
        public static bool TouchMoveHeld;
        public static bool TouchMarkDown;   // one-shot, cleared after one frame
        public static bool TouchKickHeld;
        public static bool TouchKickUp;     // one-shot

        public static bool MoveHeld  => Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) || TouchMoveHeld;
        public static bool MarkDown  => Input.GetKeyDown(KeyCode.Space) || TouchMarkDown;
        // Desktop testing uses a dedicated key (K), never the mouse — a
        // mouse button doubles as "click anywhere on this canvas," which is
        // exactly the bug that used to fire kicks from ordinary taps.
        public static bool KickHeld  => Input.GetKey(KeyCode.K) || TouchKickHeld;
        public static bool KickUp    => Input.GetKeyUp(KeyCode.K) || TouchKickUp;

        // Called once per frame, after every AFLPlayer/AFLGameManager Update
        // has had a chance to read this frame's one-shot touch flags — see
        // AFLTouchBridge.LateUpdate().
        internal static void ClearOneShotTouchFlags()
        {
            TouchMarkDown = false;
            TouchKickUp = false;
        }
    }

    public enum MarkGrade { Screamer, Clunk, Fumble, Spoil, Miss }
}
