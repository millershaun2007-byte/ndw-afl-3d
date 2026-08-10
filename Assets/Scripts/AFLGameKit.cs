// ---------------------------------------------------------------------------
//  AFLGameKit.cs  —  single-file 3D AFL starter kit for Unity 2021.3 LTS+
//  Uses the legacy Input Manager so it runs with zero package setup.
//  All components appear in the "Add Component > AFL" menu.
// ---------------------------------------------------------------------------
using System.Collections.Generic;
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
        public float minMarkDistance = 15f;        // AFL rule of thumb
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
            Rb.drag = drag;                 // Unity 6: rename to linearDamping
            Rb.angularDrag = angularDrag;
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

            Vector3 p = Rb.position, v = Rb.velocity;
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
            Rb.isKinematic = true;
            Rb.velocity = Vector3.zero;
            Rb.angularVelocity = Vector3.zero;
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
            Rb.velocity = velocity;
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
            Vector3 v = Rb.velocity;
            v += new Vector3(Random.Range(-1f, 1f), Random.Range(0f, 1.2f), Random.Range(-1f, 1f)) *
                 bounceRandomness * v.magnitude * 0.35f;
            Rb.velocity = v;
        }

        public void ResetTo(Vector3 pos)
        {
            transform.SetParent(null, true);
            Carrier = null; WasKicked = false; HasBounced = false;
            Rb.isKinematic = false;
            Rb.position = pos; Rb.velocity = Vector3.zero; Rb.angularVelocity = Vector3.zero;
        }
    }

    // =======================================================================
    //  PLAYER  (user controlled or AI driven)
    // =======================================================================
    [AddComponentMenu("AFL/AFL Player")]
    [RequireComponent(typeof(CharacterController))]
    public class AFLPlayer : MonoBehaviour
    {
        public enum Team { Home, Away }
        public static readonly List<AFLPlayer> All = new List<AFLPlayer>();

        [Header("Identity")]
        public Team team = Team.Home;
        public bool isUserControlled;
        public Animator animator;
        public Transform ballHold;          // empty at chest/hands
        public Transform handsAnchor;       // empty at top of reach

        [Header("Movement")]
        public float walkSpeed = 3.4f;
        public float runSpeed = 7.0f;
        public float sprintSpeed = 9.3f;
        public float acceleration = 24f;
        public float turnSmoothTime = 0.08f;
        public float gravity = -24f;        // snappier than real g, feels better

        [Header("Jump / Marking")]
        public float jumpHeight = 1.15f;
        public float standingReach = 2.20f;   // ground -> fingertips, feet down
        public float catchRadius = 1.25f;
        public float perfectWindow = 0.09f;   // ±s from apex = screamer
        public float goodWindow = 0.20f;
        public float lateWindow = 0.34f;
        public float bidLifetime = 1.4f;      // press must be this recent

        [Header("Disposal")]
        public float kickChargeTime = 0.9f;
        public float minKickSpeed = 13f;
        public float maxKickSpeed = 29f;
        public float minKickAngle = 22f;
        public float maxKickAngle = 41f;
        public float handballSpeed = 13f;
        public float baseAccuracyError = 1.5f;   // degrees

        [Header("Contest tuning")]
        public float strength = 0.5f;            // 0..1, biases 50/50s

        // runtime
        CharacterController _cc;
        Vector3 _horizVel;
        float _vertVel, _turnVel;
        float _bidTime = -99f, _timingError = 99f;
        float _kickCharge;
        Camera _cam;

        public bool IsAirborne { get; private set; }
        public bool HasBall => AFLBall.Instance && AFLBall.Instance.Carrier == this;
        public float KickCharge => _kickCharge;
        public float LastTimingError => _timingError;
        public Vector3 Velocity => _horizVel;

        float JumpVelocity => Mathf.Sqrt(2f * jumpHeight * -gravity);
        public float TimeToApex => JumpVelocity / -gravity;

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _cam = Camera.main;
            if (!ballHold)    ballHold    = CreateAnchor("BallHold", new Vector3(0.35f, 1.15f, 0.35f));
            if (!handsAnchor) handsAnchor = CreateAnchor("Hands",    new Vector3(0f, standingReach, 0.25f));
        }

        Transform CreateAnchor(string n, Vector3 local)
        {
            var t = new GameObject(n).transform;
            t.SetParent(transform, false);
            t.localPosition = local;
            return t;
        }

        void Update()
        {
            if (isUserControlled) HandleInput();
            ApplyMotion();
            DriveAnimator();
        }

        // ---- input ---------------------------------------------------------
        void HandleInput()
        {
            Vector2 mv = AFLInput.Move;
            Vector3 camF = Vector3.forward, camR = Vector3.right;
            if (_cam)
            {
                camF = Vector3.ProjectOnPlane(_cam.transform.forward, Vector3.up).normalized;
                camR = Vector3.ProjectOnPlane(_cam.transform.right, Vector3.up).normalized;
            }
            Vector3 wish = (camF * mv.y + camR * mv.x);
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            float target = AFLInput.Sprint ? sprintSpeed : (wish.magnitude > 0.6f ? runSpeed : walkSpeed);
            if (HasBall) target *= 0.94f;                       // carrying costs a touch
            SetMoveIntent(wish, target);

            if (AFLInput.MarkDown) AttemptContest();
            if (AFLInput.Tackle)   AttemptTackle();

            if (HasBall)
            {
                if (AFLInput.KickHeld) _kickCharge = Mathf.Clamp01(_kickCharge + Time.deltaTime / kickChargeTime);
                if (AFLInput.KickUp && _kickCharge > 0.05f) { Kick(_kickCharge, AimDirection()); _kickCharge = 0f; }
                if (AFLInput.Handball) Handball(AimDirection());
            }
            else _kickCharge = 0f;
        }

        Vector3 AimDirection()
        {
            if (_cam) return Vector3.ProjectOnPlane(_cam.transform.forward, Vector3.up).normalized;
            return transform.forward;
        }

        // ---- movement (also used by the AI brain) ---------------------------
        Vector3 _wishDir; float _wishSpeed;
        public void SetMoveIntent(Vector3 dir, float speed) { _wishDir = dir; _wishSpeed = speed; }

        void ApplyMotion()
        {
            Vector3 desired = _wishDir * _wishSpeed;
            _horizVel = Vector3.MoveTowards(_horizVel, desired, acceleration * Time.deltaTime);

            if (_cc.isGrounded && _vertVel < 0f) { _vertVel = -2f; IsAirborne = false; }
            _vertVel += gravity * Time.deltaTime;

            if (_horizVel.sqrMagnitude > 0.04f)
            {
                float want = Mathf.Atan2(_horizVel.x, _horizVel.z) * Mathf.Rad2Deg;
                float yaw = Mathf.SmoothDampAngle(transform.eulerAngles.y, want, ref _turnVel, turnSmoothTime);
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            }

            _cc.Move((_horizVel + Vector3.up * _vertVel) * Time.deltaTime);
        }

        void DriveAnimator()
        {
            if (!animator) return;
            animator.SetFloat("Speed", _horizVel.magnitude, 0.1f, Time.deltaTime);
            animator.SetBool("Airborne", IsAirborne);
            animator.SetBool("HasBall", HasBall);
            animator.SetFloat("KickCharge", _kickCharge);
        }

        // ---- THE TIMED JUMP -------------------------------------------------
        public void AttemptContest()
        {
            var ball = AFLBall.Instance;
            _timingError = 99f;

            if (ball && ball.InFlight)
            {
                float feet = transform.position.y;
                float ceiling = feet + standingReach + jumpHeight + 0.35f;

                if (ball.PredictReach(transform.position, _horizVel, catchRadius + 0.7f,
                                      feet + 0.4f, ceiling, 2.5f, out float t, out Vector3 pt))
                {
                    // If the ball arrives above standing reach you must jump, and the
                    // press should land the APEX of the jump on the ball. If it's low,
                    // you just need hands up ~0.12s before it gets there.
                    bool needsJump = pt.y > feet + standingReach - 0.30f;
                    float idealLead = needsJump ? TimeToApex : 0.12f;
                    _timingError = t - idealLead;          // + = too early, - = too late
                    _bidTime = Time.time;

                    if (needsJump && _cc.isGrounded)
                    {
                        _vertVel = JumpVelocity;
                        IsAirborne = true;
                        if (animator) animator.SetTrigger("Jump");
                    }
                    return;
                }
            }

            // no ball in the air: this press is a ground gather / loose ball dive
            _bidTime = Time.time;
            _timingError = 0.15f;
        }

        public bool CanReach(Vector3 ballPos)
        {
            float feet = transform.position.y;
            float handsY = handsAnchor ? handsAnchor.position.y : feet + standingReach;
            if (ballPos.y > handsY + 0.45f) return false;
            if (ballPos.y < feet - 0.35f) return false;
            Vector3 d = ballPos - transform.position; d.y = 0f;
            return d.sqrMagnitude <= catchRadius * catchRadius * 1.6f;
        }

        /// 0 = no chance, 1 = perfectly timed screamer.
        public float EvaluateBid(AFLBall ball)
        {
            if (Time.time - _bidTime > bidLifetime) return 0.05f;   // wasn't even going for it

            float e = Mathf.Abs(_timingError);
            float grade;
            if (e <= perfectWindow)      grade = Mathf.Lerp(1.00f, 0.88f, e / perfectWindow);
            else if (e <= goodWindow)    grade = Mathf.Lerp(0.86f, 0.62f, Mathf.InverseLerp(perfectWindow, goodWindow, e));
            else if (e <= lateWindow)    grade = Mathf.Lerp(0.58f, 0.26f, Mathf.InverseLerp(goodWindow, lateWindow, e));
            else                         grade = 0.10f;

            // facing the flight of the ball is worth something
            Vector3 toBall = ball.Rb.position - transform.position; toBall.y = 0f;
            float facing = Vector3.Dot(transform.forward, toBall.normalized);
            grade *= Mathf.Lerp(0.75f, 1.05f, Mathf.InverseLerp(-1f, 1f, facing));

            // strength decides the scrappy ones, plus a little chaos
            grade += (strength - 0.5f) * 0.12f;
            grade += Random.Range(-0.05f, 0.05f);

            return Mathf.Clamp01(grade);
        }

        public void OnContestResult(AFLBall ball, MarkGrade grade)
        {
            switch (grade)
            {
                case MarkGrade.Screamer:
                case MarkGrade.Clunk:
                    ball.Attach(this);
                    AFLGameManager.Instance?.AnnounceMark(this, grade);
                    if (animator) animator.SetTrigger(grade == MarkGrade.Screamer ? "Screamer" : "Mark");
                    break;

                case MarkGrade.Fumble:                 // gathered, no mark paid
                    ball.Attach(this);
                    AFLGameManager.Instance?.Announce(team + " gathers");
                    break;

                case MarkGrade.Spoil:
                    ball.Spoil(handsAnchor ? handsAnchor.position : transform.position);
                    AFLGameManager.Instance?.Announce("Spoiled!");
                    if (animator) animator.SetTrigger("Spoil");
                    break;
            }
            _bidTime = -99f;
        }

        // ---- disposal -------------------------------------------------------
        public void Kick(float power, Vector3 flatDir)
        {
            var ball = AFLBall.Instance;
            if (!ball || ball.Carrier != this) return;

            power = Mathf.Clamp01(power);
            float speed = Mathf.Lerp(minKickSpeed, maxKickSpeed, power);
            float angle = Mathf.Lerp(minKickAngle, maxKickAngle, power);

            // accuracy falls away when you're sprinting or going for the big one
            float err = baseAccuracyError + power * 2.0f + _horizVel.magnitude * 0.25f;
            Quaternion yawErr = Quaternion.Euler(0f, Random.Range(-err, err), 0f);
            Vector3 dir = yawErr * Quaternion.AngleAxis(-angle, Vector3.Cross(Vector3.up, flatDir)) * flatDir;

            ball.Release(dir.normalized * speed, transform.right * -18f, this, true);
            if (animator) animator.SetTrigger("Kick");
        }

        public void Handball(Vector3 flatDir)
        {
            var ball = AFLBall.Instance;
            if (!ball || ball.Carrier != this) return;
            Vector3 dir = (flatDir + Vector3.up * 0.18f).normalized;
            ball.Release(dir * handballSpeed, Vector3.zero, this, false);
            if (animator) animator.SetTrigger("Handball");
        }

        void AttemptTackle()
        {
            var ball = AFLBall.Instance;
            if (!ball || ball.Carrier == null || ball.Carrier == this) return;
            var victim = ball.Carrier;
            if (victim.team == team) return;
            if (Vector3.Distance(victim.transform.position, transform.position) > 1.8f) return;

            bool held = Random.value < 0.5f + (strength - victim.strength) * 0.5f;
            if (held)
            {
                AFLGameManager.Instance?.Announce("Holding the ball!");
                ball.Release(victim.transform.forward * 4f + Vector3.up * 3f, Vector3.zero, victim, false);
            }
            else
            {
                AFLGameManager.Instance?.Announce("Ball spills free");
                ball.Release(Random.insideUnitSphere.normalized * 6f + Vector3.up * 2f, Vector3.zero, victim, false);
            }
        }

        public void SetControlled(bool on)
        {
            isUserControlled = on;
            if (!on) SetMoveIntent(Vector3.zero, 0f);
        }
    }

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
        public float distance = 7.5f;
        public float height = 2.6f;
        [Range(0f, 1f)] public float ballBias = 0.35f;      // lean toward the ball in flight
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
        float _yaw, _pitch = 14f;
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

    // =======================================================================
    //  SCORING ZONES  — 4 box triggers between the posts, no rigidbody needed
    // =======================================================================
    [AddComponentMenu("AFL/AFL Scoring Zone")]
    public class AFLScoringZone : MonoBehaviour
    {
        public enum ScoreType { Goal, Behind }
        public ScoreType type = ScoreType.Goal;
        public AFLPlayer.Team scoringTeam = AFLPlayer.Team.Home;

        void Reset()
        {
            var c = GetComponent<Collider>();
            if (c) c.isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            var ball = other.GetComponentInParent<AFLBall>();
            if (ball == null) return;
            AFLGameManager.Instance?.RegisterScore(scoringTeam, type, ball);
        }
    }

    // =======================================================================
    //  GAME MANAGER  — score, clock, centre bounce, player switching, HUD
    // =======================================================================
    [AddComponentMenu("AFL/AFL Game Manager")]
    public class AFLGameManager : MonoBehaviour
    {
        public static AFLGameManager Instance { get; private set; }

        [Header("Refs")]
        public AFLBall ball;
        public AFLBroadcastCamera cam;
        public Transform centreCircle;
        public AFLPlayer.Team userTeam = AFLPlayer.Team.Home;

        [Header("Match")]
        public int quarters = 4;
        public float quarterSeconds = 300f;
        public float bounceDelay = 2.5f;

        public int HomeGoals { get; private set; }
        public int HomeBehinds { get; private set; }
        public int AwayGoals { get; private set; }
        public int AwayBehinds { get; private set; }
        public int Quarter { get; private set; } = 1;
        public float TimeLeft { get; private set; }

        public int HomePoints => HomeGoals * 6 + HomeBehinds;
        public int AwayPoints => AwayGoals * 6 + AwayBehinds;

        AFLPlayer _controlled;
        string _message = "";
        float _messageUntil, _scoreLock, _restartAt = -1f;
        GUIStyle _big, _small;

        void Awake()
        {
            Instance = this;
            TimeLeft = quarterSeconds;
            if (!ball) ball = FindObjectOfType<AFLBall>();
            if (!cam) cam = FindObjectOfType<AFLBroadcastCamera>();
        }

        void Start()
        {
            var first = NearestTeammateToBall(userTeam, null);
            if (first) TakeControl(first);
            if (cam && ball) cam.ball = ball;
        }

        void Update()
        {
            // clock
            if (TimeLeft > 0f)
            {
                TimeLeft -= Time.deltaTime;
                if (TimeLeft <= 0f)
                {
                    TimeLeft = 0f;
                    Announce(Quarter >= quarters ? "FULL TIME" : "End of quarter " + Quarter);
                    if (Quarter < quarters) { Quarter++; TimeLeft = quarterSeconds; QueueCentreBounce(); }
                }
            }

            // auto switch to whoever is closest to the play
            if (AFLInput.Switch)
            {
                var next = NearestTeammateToBall(userTeam, _controlled);
                if (next) TakeControl(next);
            }
            if (ball && ball.Carrier != null && ball.Carrier.team == userTeam && ball.Carrier != _controlled)
                TakeControl(ball.Carrier);   // you always control the player who wins it

            if (_restartAt > 0f && Time.time >= _restartAt) DoCentreBounce();
        }

        public void TakeControl(AFLPlayer p)
        {
            if (_controlled) _controlled.SetControlled(false);
            _controlled = p;
            _controlled.SetControlled(true);
            if (cam) cam.SetTarget(p.transform);
        }

        AFLPlayer NearestTeammateToBall(AFLPlayer.Team t, AFLPlayer exclude)
        {
            if (!ball) return null;
            AFLPlayer best = null; float bestD = float.MaxValue;
            foreach (var p in AFLPlayer.All)
            {
                if (p.team != t || p == exclude) continue;
                float d = (p.transform.position - ball.transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = p; }
            }
            return best;
        }

        public void RegisterScore(AFLPlayer.Team t, AFLScoringZone.ScoreType type, AFLBall b)
        {
            if (b.Carrier != null) return;
            if (Time.time < _scoreLock) return;
            _scoreLock = Time.time + 2f;

            bool cleanGoal = type == AFLScoringZone.ScoreType.Goal
                          && b.WasKicked && !b.HasBounced
                          && b.LastDisposer != null && b.LastDisposer.team == t;

            if (cleanGoal)
            {
                if (t == AFLPlayer.Team.Home) HomeGoals++; else AwayGoals++;
                Announce(t + " GOAL!  6 points");
            }
            else
            {
                if (t == AFLPlayer.Team.Home) HomeBehinds++; else AwayBehinds++;
                Announce(t + " behind  1 point");
            }
            QueueCentreBounce();
        }

        void QueueCentreBounce() { _restartAt = Time.time + bounceDelay; }

        void DoCentreBounce()
        {
            _restartAt = -1f;
            Vector3 c = centreCircle ? centreCircle.position : Vector3.zero;
            if (ball) ball.ResetTo(c + Vector3.up * 8f);   // umpire's bounce
            Announce("Centre bounce");
            var p = NearestTeammateToBall(userTeam, null);
            if (p) TakeControl(p);
        }

        public void AnnounceMark(AFLPlayer p, MarkGrade g)
        {
            Announce(g == MarkGrade.Screamer
                ? "SPECKY!  Mark to " + p.team + "  (" + p.LastTimingError.ToString("+0.00;-0.00") + "s)"
                : "Mark to " + p.team + "  (" + p.LastTimingError.ToString("+0.00;-0.00") + "s)");
        }

        public void Announce(string msg, float seconds = 2.5f)
        {
            _message = msg;
            _messageUntil = Time.time + seconds;
        }

        // ---- quick placeholder HUD; swap for UI Toolkit / uGUI later --------
        void OnGUI()
        {
            if (_big == null)
            {
                _big = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold };
                _small = new GUIStyle(GUI.skin.label) { fontSize = 16 };
            }

            GUI.Label(new Rect(20, 14, 900, 40),
                string.Format("HOME {0}.{1} ({2})     AWAY {3}.{4} ({5})",
                    HomeGoals, HomeBehinds, HomePoints, AwayGoals, AwayBehinds, AwayPoints), _big);

            GUI.Label(new Rect(20, 52, 400, 24),
                string.Format("Q{0}   {1:00}:{2:00}", Quarter, Mathf.FloorToInt(TimeLeft / 60f), Mathf.FloorToInt(TimeLeft % 60f)), _small);

            if (Time.time < _messageUntil)
                GUI.Label(new Rect(20, 78, 800, 30), _message, _small);

            if (_controlled != null && _controlled.HasBall && _controlled.KickCharge > 0f)
            {
                GUI.Box(new Rect(20, Screen.height - 46, 240, 22), GUIContent.none);
                GUI.Box(new Rect(22, Screen.height - 44, 236 * _controlled.KickCharge, 18), GUIContent.none);
                GUI.Label(new Rect(270, Screen.height - 48, 300, 24), "Kick power", _small);
            }

            GUI.Label(new Rect(Screen.width - 300, Screen.height - 110, 300, 110),
                "WASD move · Shift sprint\nSpace jump/mark/gather\nLMB hold+release kick\nRMB handball · E tackle · Q switch", _small);
        }
    }

    // =======================================================================
    //  BOT BRAIN  — chase, contest with human-ish timing error, kick to goal
    // =======================================================================
    [AddComponentMenu("AFL/AFL Bot Brain")]
    [RequireComponent(typeof(AFLPlayer))]
    public class AFLBotBrain : MonoBehaviour
    {
        [Header("Behaviour")]
        public Transform attackingGoal;
        [Range(0f, 1f)] public float skill = 0.6f;      // drives jump-timing error
        public float chaseRadius = 40f;
        public float kickRange = 45f;
        public float holdBeforeDisposal = 0.9f;

        AFLPlayer _p;
        Vector3 _home;
        bool _committed;
        float _recommitAt, _gotBallAt;

        void Awake()
        {
            _p = GetComponent<AFLPlayer>();
            _home = transform.position;
        }

        void Update()
        {
            if (_p.isUserControlled) return;
            var ball = AFLBall.Instance;
            if (ball == null) return;

            if (_p.HasBall) { WithBall(ball); return; }
            _gotBallAt = 0f;

            if (ball.Carrier != null && ball.Carrier.team != _p.team) { Chase(ball.Carrier.transform.position, ball); return; }
            if (ball.InFlight) { ContestFlight(ball); return; }

            float d = Vector3.Distance(transform.position, ball.transform.position);
            if (d < chaseRadius) Chase(ball.transform.position, ball);
            else MoveTo(_home, _p.walkSpeed);
        }

        void WithBall(AFLBall ball)
        {
            if (_gotBallAt == 0f) _gotBallAt = Time.time;
            Vector3 goal = attackingGoal ? attackingGoal.position : transform.position + transform.forward * 50f;
            Vector3 dir = goal - transform.position; dir.y = 0f;
            float dist = dir.magnitude;

            MoveTo(goal, _p.runSpeed);

            if (Time.time - _gotBallAt > holdBeforeDisposal)
            {
                float power = Mathf.Clamp01(dist / kickRange);
                if (dist < 12f) _p.Handball(dir.normalized);
                else _p.Kick(Mathf.Lerp(0.45f, 1f, power) * Mathf.Lerp(0.8f, 1f, skill), dir.normalized);
                _gotBallAt = 0f;
            }
        }

        void Chase(Vector3 pos, AFLBall ball)
        {
            MoveTo(pos, _p.runSpeed);
            if (Vector3.Distance(transform.position, ball.transform.position) < 1.6f &&
                ball.Carrier == null && Random.value < 0.15f)
                _p.AttemptContest();
        }

        void ContestFlight(AFLBall ball)
        {
            // run to where it's coming down
            Vector3 landing = PredictLanding(ball, transform.position.y);
            MoveTo(landing, _p.sprintSpeed);

            if (Time.time > _recommitAt) _committed = false;
            if (_committed) return;

            float feet = transform.position.y;
            float ceiling = feet + _p.standingReach + _p.jumpHeight + 0.35f;
            if (!ball.PredictReach(transform.position, _p.Velocity, _p.catchRadius + 0.6f,
                                   feet + 0.4f, ceiling, 2.5f, out float t, out Vector3 pt)) return;

            bool needsJump = pt.y > feet + _p.standingReach - 0.30f;
            float ideal = needsJump ? _p.TimeToApex : 0.12f;
            float jitter = (1f - skill) * 0.22f;
            float pressAt = ideal + Random.Range(-jitter, jitter);

            if (t <= pressAt)
            {
                _p.AttemptContest();
                _committed = true;
                _recommitAt = Time.time + 1.2f;
            }
        }

        static Vector3 PredictLanding(AFLBall ball, float groundY)
        {
            Vector3 p = ball.Rb.position, v = ball.Rb.velocity;
            const float dt = 0.05f;
            float k = Mathf.Clamp01(1f - ball.drag * dt);
            for (int i = 0; i < 160; i++)
            {
                v += Physics.gravity * dt;
                v *= k;
                p += v * dt;
                if (p.y <= groundY) break;
            }
            p.y = groundY;
            return p;
        }

        void MoveTo(Vector3 worldPos, float speed)
        {
            Vector3 d = worldPos - transform.position; d.y = 0f;
            if (d.sqrMagnitude < 0.6f) { _p.SetMoveIntent(Vector3.zero, 0f); return; }
            _p.SetMoveIntent(d.normalized, speed);
        }
    }
}
