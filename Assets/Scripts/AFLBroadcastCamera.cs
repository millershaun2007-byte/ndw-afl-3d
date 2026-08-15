using UnityEngine;

namespace AFL
{
    // =======================================================================
    //  BROADCAST CAMERA — fixed cut per beat, never follows (2026-08-11 rewrite)
    // =======================================================================
    // Per the rewrite brief on issue #1: the previous continuous-follow
    // camera (SmoothDamp position, LerpAngle auto-align to velocity) could
    // not cope with direction changes — wrong-way-round yaw after a control
    // switch, sinking to grass level, clipping into players. Shaun's own
    // read: "the camera cannot cope with direction changes." With
    // forward-only player movement (see AFLPlayer) there ARE no direction
    // changes left mid-beat, so the fix is to delete the chase logic
    // entirely rather than keep tuning it — CutTo() is now the only thing
    // this class does. A cut cannot end up pointing the wrong way; a chase
    // could always drift there. There is no LateUpdate follow any more.
    [AddComponentMenu("AFL/AFL Broadcast Camera")]
    [RequireComponent(typeof(Camera))]
    public class AFLBroadcastCamera : MonoBehaviour
    {
        [Header("Framing")]
        public Vector3 pivotOffset = new Vector3(0f, 1.55f, 0f);
        public float distance = 6f;
        public float height = 1.9f;
        public float fixedFov = 55f;

        [Header("Collision")]
        public LayerMask collisionMask = 1;
        public float collisionRadius = 0.32f;
        public float collisionBuffer = 0.25f;

        // Never frame the edge of the ground plane (issue #1: "the camera
        // must also never frame the edge of the ground plane") — clamp the
        // pivot used for framing to stay well inside the field bounds, even
        // if the actual target transform is momentarily near/over the edge
        // (e.g. mid-reset after an out-of-bounds invariant catch).
        public float fieldHalfWidth = 18f;
        public float fieldHalfLength = 23f;
        float _safeMargin = 4f;

        Camera _cam;

        void Awake()
        {
            _cam = GetComponent<Camera>();
            _cam.fieldOfView = fixedFov;
        }

        // ---- Punch — a bounded, self-clearing impact shake ----------------
        // Does not violate the "no follow, cut only" rule above: it never
        // tracks a moving target and never runs longer than _punchDuration,
        // so it can't drift the way the old chase camera did. It only ever
        // offsets whatever CutTo/CutToSide already framed this beat, and the
        // next beat's cut always sets transform absolutely, overwriting any
        // leftover offset outright — there is nothing for this to leak into.
        Vector3 _punchBasePos;
        Quaternion _punchBaseRot;
        float _punchStartedAt = -99f;
        float _punchStrength;
        const float PunchDuration = 0.18f;

        /// One-shot camera nudge/shake for an impactful moment (e.g. a
        /// spoiled mark) — call right when the moment resolves, on top of
        /// whichever CutTo/CutToSide is already framing the beat.
        public void Punch(float strength = 0.35f)
        {
            _punchBasePos = transform.position;
            _punchBaseRot = transform.rotation;
            _punchStartedAt = Time.time;
            _punchStrength = strength;
        }

        // LateUpdate (not Update) so this always applies after whichever
        // beat script may have called CutTo/CutToSide this same frame,
        // rather than racing script execution order.
        void LateUpdate()
        {
            if (_punchStartedAt < 0f) return;
            float t = (Time.time - _punchStartedAt) / PunchDuration;
            if (t >= 1f) { _punchStartedAt = -99f; return; }
            float env = (1f - t) * Mathf.Sin(t * Mathf.PI * 3f);   // quick decaying shake
            transform.position = _punchBasePos + transform.forward * env * _punchStrength * 0.5f;
            transform.rotation = _punchBaseRot * Quaternion.Euler(env * _punchStrength * 4f, 0f, 0f);
        }

        Vector3 ClampToField(Vector3 p)
        {
            p.x = Mathf.Clamp(p.x, -fieldHalfWidth + _safeMargin, fieldHalfWidth - _safeMargin);
            p.z = Mathf.Clamp(p.z, -fieldHalfLength + _safeMargin, fieldHalfLength - _safeMargin);
            return p;
        }

        /// Hard cut to a fixed framing of `target`, looking roughly toward
        /// `lookTowards` if given (e.g. the goal during a kick beat) or
        /// just using the target's own facing otherwise. No smoothing, no
        /// per-frame update after this — call again at the next beat.
        public void CutTo(Transform target, Vector3? lookTowardsFlatDir = null)
        {
            if (!target) return;
            Vector3 pivot = ClampToField(target.position + pivotOffset);

            Vector3 facing = lookTowardsFlatDir ?? target.forward;
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.01f) facing = Vector3.forward;
            facing.Normalize();

            // Sit behind the target relative to the direction we want
            // framed (their own facing, or the goal for a kick beat), same
            // over-the-shoulder broadcast angle as before, just computed
            // once instead of every frame.
            Vector3 back = -facing;
            Vector3 wanted = pivot + back * distance + Vector3.up * height;

            float allowed = distance;
            Vector3 from = pivot + Vector3.up * height * 0.5f;
            if (Physics.SphereCast(from, collisionRadius, (wanted - from).normalized,
                                   out RaycastHit hit, Vector3.Distance(from, wanted),
                                   collisionMask, QueryTriggerInteraction.Ignore)
                && hit.collider.transform.root != target)
                allowed = Mathf.Max(1.6f, hit.distance - collisionBuffer);

            transform.position = pivot + back * allowed + Vector3.up * height;
            transform.rotation = Quaternion.LookRotation(pivot + Vector3.up * 0.35f - transform.position, Vector3.up);
        }

        /// Side-on framing for a kick/mark beat where seeing both the
        /// player and the ball's flight matters more than an over-the-
        /// shoulder angle — fixed, no smoothing.
        public void CutToSide(Transform target, Vector3 flatDir)
        {
            if (!target) return;
            Vector3 pivot = ClampToField(target.position + pivotOffset);
            flatDir.y = 0f;
            if (flatDir.sqrMagnitude < 0.01f) flatDir = Vector3.forward;
            flatDir.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, flatDir);

            Vector3 pos = pivot - flatDir * 3.5f + side * 5.5f + Vector3.up * height;
            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(pivot + Vector3.up * 0.3f - pos, Vector3.up);
        }
    }
}
