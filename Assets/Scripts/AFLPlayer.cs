using System.Collections.Generic;
using UnityEngine;

namespace AFL
{
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
            if (!ballHold) ballHold = CreateAnchor("BallHold", new Vector3(0.35f, 1.15f, 0.35f));

            // Real fix (2026-08-10 code review): standingReach and
            // handsAnchor used to be two independently-set facts about the
            // same thing — where this character's hands actually are —
            // which is exactly the "one fact written down in two places"
            // bug class that broke marking in every earlier version of this
            // game (2D: hands drawn at -sz*1.18 vs ball placed at -sz*0.05;
            // first Unity version: root rotated correctly while a Visual
            // child carried a stale 180-degree yaw offset). If a prefab
            // already positions a Hands anchor, that anchor wins and
            // standingReach is derived from it, not the other way around —
            // an artist positioning hands on a model automatically becomes
            // correct physics, instead of needing separately-tuned agreement.
            if (handsAnchor) standingReach = handsAnchor.localPosition.y;
            else handsAnchor = CreateAnchor("Hands", new Vector3(0f, standingReach, 0.25f));
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
}
