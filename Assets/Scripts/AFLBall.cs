using UnityEngine;

namespace AFL
{
    // =======================================================================
    //  BALL
    // =======================================================================
    [AddComponentMenu("AFL/AFL Ball")]
    [RequireComponent(typeof(Rigidbody))]
    public class AFLBall : MonoBehaviour
    {
        public static AFLBall Instance { get; private set; }

        [Header("Physics")]
        public float mass = 0.45f;
        public float drag = 0.06f;
        public float angularDrag = 0.35f;
        public float bounceRandomness = 0.45f;     // oval ball = ugly bounces
        public LayerMask groundMask = ~0;

        [Header("Contest")]
        public LayerMask playerMask;
        public float contestScanRadius = 2.2f;
        public float markThreshold = 0.55f;        // bid needed to hold a mark
        public float gatherThreshold = 0.30f;      // below this it spills
        // Was 15 (real AFL rule of thumb) — nearly 40% of this 45-unit-long
        // ground, which made most clearance kicks unmarkable. Issue #1: "on
        // a 45m ground clean marks rarely register." 8 keeps the rule
        // meaningful (a genuine short give-up handball still can't be
        // marked) without making the actual forward-kick-to-mark loop this
        // whole rebuild is built around structurally rare.
        public float minMarkDistance = 8f;
        public float contestCooldown = 0.35f;

        public Rigidbody Rb { get; private set; }
        public AFLPlayer Carrier { get; private set; }
        public AFLPlayer LastDisposer { get; private set; }
        public bool WasKicked { get; private set; }
        public bool HasBounced { get; private set; }
        public bool InFlight => Carrier == null && !Rb.isKinematic;
        public bool IsMarkable => InFlight && WasKicked && !HasBounced &&
                                  Vector3.Distance(_launchPoint, Rb.position) >= minMarkDistance;

        Vector3 _launchPoint;
        float _contestLockUntil;
        readonly Collider[] _hits = new Collider[16];

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

        void FixedUpdate()
        {
            if (Carrier != null || Time.time < _contestLockUntil) return;
            ResolveContest();
        }

        // ---- trajectory prediction: the core of the jump-timing system -----
        public bool PredictReach(Vector3 origin, Vector3 originVel, float radiusXZ,
                                 float minY, float maxY, float maxTime,
                                 out float hitTime, out Vector3 hitPoint)
        {
            hitTime = 0f; hitPoint = Vector3.zero;
            if (!InFlight) return false;

            Vector3 p = Rb.position, v = Rb.linearVelocity;
            const float dt = 0.04f;
            float dragK = Mathf.Clamp01(1f - drag * dt);

            for (float t = dt; t <= maxTime; t += dt)
            {
                v += Physics.gravity * dt;
                v *= dragK;
                p += v * dt;
                if (p.y < -2f) break;

                if (p.y >= minY && p.y <= maxY)
                {
                    Vector3 o = origin + originVel * t;            // lead the runner
                    float dx = p.x - o.x, dz = p.z - o.z;
                    if (dx * dx + dz * dz <= radiusXZ * radiusXZ)
                    {
                        hitTime = t; hitPoint = p; return true;
                    }
                }
            }
            return false;
        }

        void ResolveContest()
        {
            int n = Physics.OverlapSphereNonAlloc(Rb.position, contestScanRadius, _hits, playerMask);
            AFLPlayer best = null; float bestBid = 0f;

            for (int i = 0; i < n; i++)
            {
                var p = _hits[i].GetComponentInParent<AFLPlayer>();
                if (p == null || !p.CanReach(Rb.position)) continue;
                float bid = p.EvaluateBid(this);
                if (bid > bestBid) { bestBid = bid; best = p; }
            }
            if (best == null || bestBid < 0.12f) return;   // nobody actually went for it

            _contestLockUntil = Time.time + contestCooldown;

            if (bestBid >= markThreshold && IsMarkable)
                best.OnContestResult(this, bestBid >= 0.85f && best.IsAirborne ? MarkGrade.Screamer : MarkGrade.Clunk);
            else if (bestBid >= gatherThreshold)
                best.OnContestResult(this, MarkGrade.Fumble);
            else
                best.OnContestResult(this, MarkGrade.Spoil);
        }

        // ---- possession -----------------------------------------------------
        public void Attach(AFLPlayer p)
        {
            Carrier = p;
            // Real bug, only surfaced by the automated playtest (2026-08-11)
            // — a kinematic Rigidbody can't have its velocity set at all;
            // doing it in this order logged "Setting velocity of a
            // kinematic body is not supported" on every single mark/
            // gather/fumble, which was true of the original pre-rebuild
            // code too, just never looked at closely enough to notice.
            // Zero the velocity while it's still dynamic, then go
            // kinematic.
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
            _launchPoint = transform.position;
            Rb.isKinematic = false;
            Rb.linearVelocity = velocity;
            Rb.angularVelocity = spin;
            _contestLockUntil = Time.time + 0.2f;   // no instant self-recatch
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
            // pointy ends: randomise the bounce so ground balls stay unpredictable
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
