using UnityEngine;

namespace AFL
{
    // =======================================================================
    //  BROADCAST CAMERA — smooth follow, but cuts deliberately at handoffs
    // =======================================================================
    // Rebuilt 2026-08-11 (issue #1): the old camera continuously chased
    // whoever had the ball with a ball-bias lean and a speed-driven FOV
    // punch, and separately read AFLInput.Look for manual orbit — a raw
    // mouse-axis read with no button gate, which is why it used to swing on
    // any stray pointer movement. Both are gone, not toned down. This
    // camera now just follows its current target smoothly, and CutTo()
    // snaps to a new target instantly at each control handoff instead of
    // drifting there — the actual fix for "camera left pointing the wrong
    // way after a switch," which smoothing on top of the old logic could
    // never have solved.
    [AddComponentMenu("AFL/AFL Broadcast Camera")]
    [RequireComponent(typeof(Camera))]
    public class AFLBroadcastCamera : MonoBehaviour
    {
        [Header("Target")]
        public Transform target;

        [Header("Framing")]
        public Vector3 pivotOffset = new Vector3(0f, 1.55f, 0f);
        public float distance = 6f;
        public float height = 1.9f;
        public float fixedFov = 55f;

        [Header("Feel")]
        public float positionSmooth = 0.10f;
        public float rotationSmooth = 9f;
        public float autoAlignSpeed = 2.2f;   // swings behind the runner as they move

        [Header("Collision")]
        public LayerMask collisionMask = 1;
        public float collisionRadius = 0.32f;
        public float collisionBuffer = 0.25f;

        Camera _cam;
        float _yaw;
        Vector3 _posVel, _smoothPivot, _pivotVel;
        float _currentDistance;
        bool _setShotMode;
        Transform _setShotGoal;

        void Awake()
        {
            _cam = GetComponent<Camera>();
            _cam.fieldOfView = fixedFov;
            _currentDistance = distance;
            if (target) { _yaw = target.eulerAngles.y; _smoothPivot = target.position + pivotOffset; }
        }

        void LateUpdate()
        {
            if (_setShotMode) { UpdateSetShotFraming(); return; }
            if (!target) return;

            Vector3 pivot = target.position + pivotOffset;
            _smoothPivot = Vector3.SmoothDamp(_smoothPivot, pivot, ref _pivotVel, positionSmooth);

            var p = target.GetComponent<AFLPlayer>();
            if (p && p.Velocity.sqrMagnitude > 4f)
            {
                float want = Mathf.Atan2(p.Velocity.x, p.Velocity.z) * Mathf.Rad2Deg;
                _yaw = Mathf.LerpAngle(_yaw, want, autoAlignSpeed * Time.deltaTime);
            }

            Quaternion orbit = Quaternion.Euler(8f, _yaw, 0f);
            Vector3 dir = orbit * Vector3.back;
            Vector3 wanted = _smoothPivot + dir * distance + Vector3.up * height;

            float allowed = distance;
            Vector3 from = _smoothPivot + Vector3.up * height * 0.5f;
            // Real bug, found 2026-08-11 from the "camera spawns/sits
            // inside the pack" report: collisionMask only ever covered
            // Ground, so the camera happily clipped straight through any
            // OTHER player during a contest or centre bounce (the one
            // exclusion that existed was only ever for the followed
            // player's own body, via a Ground-only mask that never saw
            // players at all). A camera clipped inside one character
            // looking through them at another reads exactly like the
            // reported "two heads, four arms" — likely the same bug, not
            // a second one. BuildScript now includes the Player layer
            // here; the target's own collider is explicitly ignored below
            // so this still doesn't reintroduce the old "camera jams into
            // its own target's back" problem.
            if (Physics.SphereCast(from, collisionRadius, (wanted - from).normalized,
                                   out RaycastHit hit, Vector3.Distance(from, wanted),
                                   collisionMask, QueryTriggerInteraction.Ignore)
                && hit.collider.transform.root != target)
                allowed = Mathf.Max(1.6f, hit.distance - collisionBuffer);

            _currentDistance = Mathf.Lerp(_currentDistance, allowed, allowed < _currentDistance ? 1f : 3f * Time.deltaTime);
            Vector3 finalPos = _smoothPivot + dir * _currentDistance + Vector3.up * height;
            transform.position = Vector3.SmoothDamp(transform.position, finalPos, ref _posVel, positionSmooth);

            Vector3 lookAt = _smoothPivot + Vector3.up * 0.35f;
            Quaternion wantRot = Quaternion.LookRotation(lookAt - transform.position, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, wantRot, rotationSmooth * Time.deltaTime);
        }

        // Instant cut — no smoothing carried over from the previous target.
        // Call this at every control handoff (centre -> carrier, carrier ->
        // receiving forward/defender, mark -> set shot).
        public void CutTo(Transform newTarget)
        {
            _setShotMode = false;
            target = newTarget;
            if (!target) return;
            _yaw = target.eulerAngles.y;
            Vector3 pivot = target.position + pivotOffset;
            _smoothPivot = pivot;
            _pivotVel = Vector3.zero;

            // An instant cut with no collision check at all was the other
            // half of the "camera inside the pack" bug — a handoff during
            // a contest (several players clustered together) could snap
            // straight into or through whoever's standing where the raw
            // orbit position lands. Same SphereCast-and-pull-in logic
            // LateUpdate() uses, just applied once instead of every frame.
            Quaternion orbit = Quaternion.Euler(8f, _yaw, 0f);
            Vector3 dir = orbit * Vector3.back;
            Vector3 wanted = pivot + dir * distance + Vector3.up * height;
            float allowed = distance;
            Vector3 from = pivot + Vector3.up * height * 0.5f;
            if (Physics.SphereCast(from, collisionRadius, (wanted - from).normalized,
                                   out RaycastHit hit, Vector3.Distance(from, wanted),
                                   collisionMask, QueryTriggerInteraction.Ignore)
                && hit.collider.transform.root != target)
                allowed = Mathf.Max(1.6f, hit.distance - collisionBuffer);
            _currentDistance = allowed;

            transform.position = pivot + dir * allowed + Vector3.up * height;
            transform.rotation = Quaternion.LookRotation(pivot + Vector3.up * 0.35f - transform.position, Vector3.up);
            _posVel = Vector3.zero;
        }

        // Behind-the-kicker framing for the set shot — fixed, no smoothing
        // or collision handling needed since nothing is moving the camera.
        public void CutToSetShot(Transform kicker, Transform goal)
        {
            _setShotMode = true;
            target = kicker;
            _setShotGoal = goal;
            UpdateSetShotFraming();
        }

        void UpdateSetShotFraming()
        {
            if (!target) return;
            Vector3 toGoal = _setShotGoal ? (_setShotGoal.position - target.position) : target.forward;
            toGoal.y = 0f;
            if (toGoal.sqrMagnitude < 0.01f) toGoal = target.forward;
            toGoal.Normalize();
            Vector3 pos = target.position - toGoal * 5.5f + Vector3.up * 2.4f;
            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(target.position + Vector3.up * 1.2f - pos, Vector3.up);
        }
    }
}
