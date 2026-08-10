using System.Globalization;
using UnityEngine;

namespace AFL
{
    // =======================================================================
    //  TOUCH BRIDGE  — HTML D-pad + action buttons -> AFLInput
    // =======================================================================
    // GameObject name must stay "TouchBridge" — the HTML control bar
    // (Assets/WebGLTemplates/Responsive/index.html) targets it by name via
    // unityInstance.SendMessage('TouchBridge', method, value).
    //
    // A D-pad, not an analog joystick, by deliberate choice: an earlier
    // version of this game shipped a freeform drag joystick and it was the
    // single clearest unfixable control problem across many real attempts
    // — precise angle-aiming is genuinely hard for a young kid on a phone
    // screen. Four discrete directions (combinable for diagonals) give real
    // 2D movement without needing that precision.
    [AddComponentMenu("AFL/AFL Touch Bridge")]
    public class AFLTouchBridge : MonoBehaviour
    {
        void LateUpdate()
        {
            AFLInput.ClearOneShotTouchFlags();
        }

        // "x,y", each -1/0/1 — the D-pad's currently-held direction combo.
        public void SetMoveVector(string csv)
        {
            var parts = csv.Split(',');
            if (parts.Length != 2) return;
            float x = float.Parse(parts[0], CultureInfo.InvariantCulture);
            float y = float.Parse(parts[1], CultureInfo.InvariantCulture);
            AFLInput.TouchMove = new Vector2(x, y);
        }

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
