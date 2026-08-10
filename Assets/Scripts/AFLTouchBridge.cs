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
    // Single hold-to-move button, not a D-pad or joystick — real-device
    // testing found the D-pad "just doesn't work for this game" (matching
    // the earlier, separate finding that an analog joystick was
    // unworkable). Every player only ever advances straight down their own
    // fixed attack lane (AFLPlayer.attackDir) while held.
    [AddComponentMenu("AFL/AFL Touch Bridge")]
    public class AFLTouchBridge : MonoBehaviour
    {
        void LateUpdate()
        {
            AFLInput.ClearOneShotTouchFlags();
        }

        public void SetMoveForwardHeld(string v) { AFLInput.TouchMoveForward = v == "1"; }

        public void MarkPressed(string _) { AFLInput.TouchMarkDown = true; }

        public void SetKickHeld(string v) { AFLInput.TouchKickHeld = v == "1"; }

        public void KickReleased(string _)
        {
            AFLInput.TouchKickHeld = false;
            AFLInput.TouchKickUp = true;
        }

        public void HandballPressed(string _) { AFLInput.TouchHandball = true; }

        public void TacklePressed(string _) { AFLInput.TouchTackle = true; }
    }
}
