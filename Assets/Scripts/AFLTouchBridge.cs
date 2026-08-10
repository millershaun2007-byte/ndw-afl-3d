using UnityEngine;

namespace AFL
{
    // =======================================================================
    //  TOUCH BRIDGE  — HTML control bar -> AFLInput
    // =======================================================================
    // GameObject name must stay "TouchBridge" — the HTML control bar
    // (Assets/WebGLTemplates/Responsive/index.html) targets it by name via
    // unityInstance.SendMessage('TouchBridge', method, value).
    //
    // Three buttons only (2026-08-11 rebuild, issue #1): MOVE, MARK, KICK.
    // HandballPressed/TacklePressed are gone along with the buttons that
    // called them — not left behind as dead methods a stray SendMessage
    // could still hit.
    [AddComponentMenu("AFL/AFL Touch Bridge")]
    public class AFLTouchBridge : MonoBehaviour
    {
        void LateUpdate()
        {
            AFLInput.ClearOneShotTouchFlags();
        }

        public void SetMoveHeld(string v) { AFLInput.TouchMoveHeld = v == "1"; }

        public void MarkPressed(string _) { AFLInput.TouchMarkDown = true; }

        public void SetKickHeld(string v) { AFLInput.TouchKickHeld = v == "1"; }

        public void KickReleased(string _)
        {
            AFLInput.TouchKickHeld = false;
            AFLInput.TouchKickUp = true;
        }
    }
}
