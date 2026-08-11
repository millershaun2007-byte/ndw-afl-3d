using UnityEngine;

namespace AFL
{
    // =======================================================================
    //  BALL
    // =======================================================================
    // Simplified 2026-08-11 for the beat rewrite (issue #1). The old
    // continuous FixedUpdate contest scan (OverlapSphere every physics
    // step, grading whoever was in range against a blind PredictReach
    // timing window) is gone — contests are now resolved explicitly by
    // AFLGameManager's beat state machine via AFLBeatPrompt, which grades
    // against the same value it draws on screen. This class is now just
    // physics + possession bookkeeping.
    [RequireComponent(typeof(Rigidbody))]
    public class AFLBall : MonoBehaviour
    {
        public static AFLBall Instance { get; private set; }

        [Header("Physics")]
        public float mass = 0.45f;
        public float drag = 0.06f;
        public float angularDrag = 0.35f;
        public float bounceRandomness = 0.45f;
        public LayerMask groundMask = ~0;

        public Rigidbody Rb { get; private set; }
        public AFLPlayer Carrier { get; private set; }
        public AFLPlayer LastDisposer { get; private set; }
        public bool WasKicked { get; private set; }
        public bool HasBounced { get; private set; }
        public bool InFlight => Carrier == null && !Rb.isKinematic;

        void Awake()
        {
            Instance = this;
            Rb = GetComponent<Rigidbody>();
            Rb.mass = mass;
            Rb.linearDamping = drag;
            Rb.angularDamping = angularDrag;
            Rb.interpolation = RigidbodyInterpolation.Interpolate;
            Rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        // Utility kept for AFLGameManager to derive a plausible mark-
        // contest ring duration from the ball's actual arc — flavour, not
        // grading. Grading always reads AFLBeatPrompt's own value, never
        // this.
        public float EstimateFallTime(float fromHeight, float maxTime = 3f)
        {
            Vector3 v = Rb.linearVelocity;
            float y = fromHeight;
            const float dt = 0.04f;
            float dragK = Mathf.Clamp01(1f - drag * dt);
            for (float t = dt; t <= maxTime; t += dt)
            {
                v.y += Physics.gravity.y * dt;
                v *= dragK;
                y += v.y * dt;
                if (y <= 0.9f) return t;
            }
            return maxTime;
        }

        public void Attach(AFLPlayer p)
        {
            Carrier = p;
            // Zero velocity while still dynamic, THEN go kinematic — a
            // kinematic Rigidbody can't have its velocity set at all
            // (logs a Unity warning every time if done the other order,
            // caught by the automated playtest early in this project).
            Rb.linearVelocity = Vector3.zero;
            Rb.angularVelocity = Vector3.zero;
            Rb.isKinematic = true;
            transform.SetParent(p.ballHold, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
        }

        public void Release(Vector3 velocity, Vector3 spin, AFLPlayer from, bool kicked)
        {
            transform.SetParent(null, true);
            Carrier = null;
            LastDisposer = from;
            WasKicked = kicked;
            HasBounced = false;
            Rb.isKinematic = false;
            Rb.linearVelocity = velocity;
            Rb.angularVelocity = spin;
        }

        public void Spoil(Vector3 fromPos)
        {
            Vector3 dir = (Rb.position - fromPos).normalized + Vector3.up * 0.8f;
            Release(dir.normalized * Random.Range(6f, 11f), Random.insideUnitSphere * 12f, null, false);
            WasKicked = false;
        }

        void OnCollisionEnter(Collision c)
        {
            if ((groundMask.value & (1 << c.gameObject.layer)) == 0) return;
            HasBounced = true;
            Vector3 v = Rb.linearVelocity;
            v += new Vector3(Random.Range(-1f, 1f), Random.Range(0f, 1.2f), Random.Range(-1f, 1f)) *
                 bounceRandomness * v.magnitude * 0.35f;
            Rb.linearVelocity = v;
        }

        public void ResetTo(Vector3 pos)
        {
            transform.SetParent(null, true);
            Carrier = null; WasKicked = false; HasBounced = false;
            Rb.isKinematic = false;
            Rb.position = pos; Rb.linearVelocity = Vector3.zero; Rb.angularVelocity = Vector3.zero;
        }
    }
}
