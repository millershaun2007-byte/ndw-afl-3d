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
    // Two buttons only (2026-08-11 beat rewrite): MOVE (held) and MARK
    // (tapped). KICK is gone along with the button that called it — see
    // AFLInput.cs.
    [AddComponentMenu("AFL/AFL Touch Bridge")]
    public class AFLTouchBridge : MonoBehaviour
    {
        void LateUpdate()
        {
            AFLInput.ClearOneShotTouchFlags();
        }

        public void SetMoveHeld(string v) { AFLInput.TouchMoveHeld = v == "1"; }

        public void MarkPressed(string _) { AFLInput.TouchMarkDown = true; }
    }
}
