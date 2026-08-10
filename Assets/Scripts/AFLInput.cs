using UnityEngine;

namespace AFL
{
    // =======================================================================
    //  INPUT WRAPPER  — swap the bodies out if you move to the Input System
    // =======================================================================
    public static class AFLInput
    {
        public static Vector2 Move   => new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        public static Vector2 Look   => new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
        public static bool Sprint    => Input.GetKey(KeyCode.LeftShift);
        public static bool MarkDown  => Input.GetKeyDown(KeyCode.Space);   // jump / mark / gather
        public static bool KickHeld  => Input.GetMouseButton(0);
        public static bool KickUp    => Input.GetMouseButtonUp(0);
        public static bool Handball  => Input.GetMouseButtonDown(1);
        public static bool Tackle    => Input.GetKeyDown(KeyCode.E);
        public static bool Switch    => Input.GetKeyDown(KeyCode.Q);
    }

    public enum MarkGrade { Screamer, Clunk, Fumble, Spoil, Miss }
}
