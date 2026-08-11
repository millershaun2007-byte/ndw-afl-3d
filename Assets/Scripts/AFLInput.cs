using UnityEngine;

namespace AFL
{
    // =======================================================================
    //  INPUT WRAPPER  — swap the bodies out if you move to the Input System
    // =======================================================================
    // Two inputs only (2026-08-11 beat rewrite): MOVE (held, forward-only
    // advance) and MARK (single tap — the one verb every beat uses,
    // graded against AFLBeatPrompt's own drawn value). KICK is gone: there
    // is no more charge-and-release, because power/aim now always come
    // from the same tap-against-the-arrow mechanic as everything else —
    // see the "one verb" section of the rewrite brief on issue #1.
    public static class AFLInput
    {
        public static bool TouchMoveHeld;
        public static bool TouchMarkDown;   // one-shot, cleared after one frame

        public static bool MoveHeld => Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) || TouchMoveHeld;
        public static bool MarkDown => Input.GetKeyDown(KeyCode.Space) || TouchMarkDown;

        internal static void ClearOneShotTouchFlags()
        {
            TouchMarkDown = false;
        }
    }
}
