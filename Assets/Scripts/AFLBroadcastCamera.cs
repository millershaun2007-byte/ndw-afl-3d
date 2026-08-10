using UnityEngine;

namespace AFL
{
    // =======================================================================
    //  BROADCAST CAMERA  — follows the player but leans on the ball
    // =======================================================================
    [AddComponentMenu("AFL/AFL Broadcast Camera")]
    [RequireComponent(typeof(Camera))]
    public class AFLBroadcastCamera : MonoBehaviour
    {
        [Header("Targets")]
        public Transform target;                       // controlled player
        public AFLBall ball;

        [Header("Framing")]
        public Vector3 pivotOffset = new Vector3(0f, 1.55f, 0f);
        // Polish pass (2026-08-10): pulled closer and lower — the original
        // 7.5/2.6/pitch-14 combo read as an elevated "blimp cam" (verified
        // via a real screenshot: horizon high in frame, characters small
        // and centrally clustered). This sits closer to a real broadcast
        // over-the-shoulder angle without losing the wide field-awareness
        // the follow camera needs.
        public float distance = 6f;
        public float height = 1.9f;
        [Range(0f, 1f)] public float ballBias = 0.4f;      // lean toward the ball in flight
        public float ballBiasMaxDistance = 45f;

        [Header("Feel")]
        public float positionSmooth = 0.10f;
        public float rotationSmooth = 9f;
        public float autoAlignSpeed = 2.2f;            // swings behind the runner
        public float yawSensitivity = 200f;
        public float pitchSensitivity = 120f;
        public float minPitch = -5f, maxPitch = 45f;

        [Header("FOV")]
        public float baseFov = 52f;
        public float maxFov = 66f;
        public float fovSpeedRef = 9f;
        public float fovSmooth = 5f;

        [Header("Collision")]
        public LayerMask collisionMask = 1;
        public float collisionRadius = 0.32f;
        public float collisionBuffer = 0.25f;

        Camera _cam;
        float _yaw, _pitch = 8f;
        Vector3 _posVel, _smoothPivot, _pivotVel;
        float _currentDistance;

        void Awake()
        {
            _cam = GetComponent<Camera>();
            if (!ball) ball = AFLBall.Instance;
            _currentDistance = distance;
            if (target) { _yaw = target.eulerAngles.y; _smoothPivot = target.position + pivotOffset; }
        }

        void LateUpdate()
        {
            if (!target) return;
            if (!ball) ball = AFLBall.Instance;

            // ---- 1. where are we looking? -----------------------------------
            Vector3 pivot = target.position + pivotOffset;
            if (ball && ball.InFlight)
            {
                float d = Vector3.Distance(pivot, ball.transform.position);
                float w = ballBias * (1f - Mathf.Clamp01(d / ballBiasMaxDistance));
                pivot = Vector3.Lerp(pivot, ball.transform.position, w);
            }
            _smoothPivot = Vector3.SmoothDamp(_smoothPivot, pivot, ref _pivotVel, positionSmooth);

            // ---- 2. orbit: manual look, otherwise drift behind the player ----
            Vector2 look = AFLInput.Look;
            if (Mathf.Abs(look.x) > 0.01f || Mathf.Abs(look.y) > 0.01f)
            {
                _yaw += look.x * yawSensitivity * Time.deltaTime;
                _pitch -= look.y * pitchSensitivity * Time.deltaTime;
            }
            else
            {
                var p = target.GetComponent<AFLPlayer>();
                if (p && p.Velocity.sqrMagnitude > 4f)
                {
                    float want = Mathf.Atan2(p.Velocity.x, p.Velocity.z) * Mathf.Rad2Deg;
                    _yaw = Mathf.LerpAngle(_yaw, want, autoAlignSpeed * Time.deltaTime);
                }
            }
            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

            // ---- 3. desired position + spherecast so we never clip a post ----
            Quaternion orbit = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 dir = orbit * Vector3.back;
            Vector3 wanted = _smoothPivot + dir * distance + Vector3.up * height;

            float allowed = distance;
            Vector3 from = _smoothPivot + Vector3.up * height * 0.5f;
            if (Physics.SphereCast(from, collisionRadius, (wanted - from).normalized,
                                   out RaycastHit hit, Vector3.Distance(from, wanted),
                                   collisionMask, QueryTriggerInteraction.Ignore))
                allowed = Mathf.Max(1.6f, hit.distance - collisionBuffer);

            _currentDistance = Mathf.Lerp(_currentDistance, allowed, allowed < _currentDistance ? 1f : 3f * Time.deltaTime);
            Vector3 finalPos = _smoothPivot + dir * _currentDistance + Vector3.up * height;
            transform.position = Vector3.SmoothDamp(transform.position, finalPos, ref _posVel, positionSmooth);

            // ---- 4. look at, with a slight lift so the ball stays framed -----
            Vector3 lookAt = _smoothPivot + Vector3.up * 0.35f;
            Quaternion wantRot = Quaternion.LookRotation(lookAt - transform.position, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, wantRot, rotationSmooth * Time.deltaTime);

            // ---- 5. speed / flight FOV --------------------------------------
            float speed = 0f;
            var pl = target.GetComponent<AFLPlayer>();
            if (pl) speed = pl.Velocity.magnitude;
            if (ball && ball.InFlight) speed = Mathf.Max(speed, ball.Rb.velocity.magnitude * 0.55f);

            float wantFov = Mathf.Lerp(baseFov, maxFov, Mathf.Clamp01(speed / fovSpeedRef));
            _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, wantFov, fovSmooth * Time.deltaTime);
        }

        public void SetTarget(Transform t)
        {
            target = t;
            if (t) _smoothPivot = t.position + pivotOffset;
        }
    }
}
