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
        public bool isRuck;                 // contests the centre throw-up
        public Animator animator;
        public Transform ballHold;          // empty at chest/hands — derived from the model, see BuildScript
        public Transform handsAnchor;       // empty at top of reach — derived from the model, see BuildScript

        // Rebuilt 2026-08-11 (issue #1): the old fixed world-space attackDir
        // lane is gone — it was the root cause of "you cannot get to the
        // ball," since it gave the player no way to steer toward anything.
        // MOVE now curves toward moveTarget (set each phase by
        // AFLGameManager/AFLBotBrain — the ball while chasing, a teammate or
        // the attacking goal while carrying). No target set falls back to
        // the ball itself, so a player is never left with nothing to aim at.
        public Transform moveTarget;

        [Header("Movement")]
        public float walkSpeed = 3.4f;
        public float runSpeed = 7.0f;
        public float sprintSpeed = 9.3f;    // used by bot chase/contest logic only — no human sprint input exists
        public float acceleration = 24f;
        public float turnSmoothTime = 0.08f;
        // Matches Physics.gravity (set in BuildScript) — was -24 against a
        // project value of -9.81, the exact "one fact in two places" bug
        // CLAUDE.md calls out: mark-timing predictions used real physics
        // gravity while the jumping player fell under a completely
        // different number, so a jump could never actually line up with
        // where the prediction said the ball would be.
        public float gravity = -14f;

        [Header("Jump / Marking")]
        public float jumpHeight = 1.15f;
        public float standingReach = 2.20f;   // ground -> fingertips, feet down — overwritten if handsAnchor is set
        public float catchRadius = 1.25f;
        // Widened from 0.09/0.20/0.34 (issue #1): those windows assumed
        // zero-latency input. A touchscreen control bar round-tripping
        // through SendMessage cannot hit a 90ms window reliably — this is
        // the "child on a touchscreen" tuning target from CLAUDE.md.
        public float perfectWindow = 0.18f;
        public float goodWindow = 0.32f;
        public float lateWindow = 0.55f;
        public float bidLifetime = 1.4f;      // press must be this recent
        // The touch bridge always biases a real press late (button press ->
        // SendMessage -> next Unity frame). Shifting the ideal press time
        // earlier by this much means a child who presses at the moment that
        // *feels* right still lands inside the good window instead of
        // always grading as slightly late.
        public float touchLatencyBias = 0.07f;

        [Header("Disposal")]
        public float kickChargeTime = 0.9f;
        // Rescaled (issue #1) for a 35x45 field with goals 40 apart: the old
        // 13-29 m/s range put a full-charge kick around 85m, meaning it
        // could clear the entire ground with no boundary rule to catch it.
        public float minKickSpeed = 10f;    // ~8m, a real short kick
        public float maxKickSpeed = 17.5f;  // ~28m, a real set-shot-range kick
        public float minKickAngle = 25f;
        public float maxKickAngle = 38f;
        public float baseAccuracyError = 1.5f;   // degrees

        [Header("Contest tuning")]
        public float strength = 0.5f;            // 0..1, biases 50/50s

        // runtime
        CharacterController _cc;
        Vector3 _horizVel;
        float _vertVel, _turnVel;
        float _bidTime = -99f, _timingError = 99f;
        bool _activeBid;
        float _kickCharge;

        // Procedural whole-body motion (2026-08-11) — none of the character
        // models have a skeleton or clips (confirmed directly in the Editor:
        // single welded mesh, zero bones, zero animation, on all six), so
        // there is no limb to animate. A hop/lean/squash cycle applied to
        // the whole "Visual" child needs no rig at all and is most of what
        // makes a character read as alive rather than a sliding statue —
        // issue #1's play report: "rigid statues that slide across the
        // ground" was the single most damning line in it.
        Transform _visual;
        bool _wasAirborne;
        float _landedAt = -99f;
        float _kickPulseAt = -99f;

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
            _visual = transform.Find("Visual");
            if (!ballHold) ballHold = CreateAnchor("BallHold", new Vector3(0.35f, 1.15f, 0.35f));

            // If BuildScript already positioned a real Hands anchor from the
            // actual model bounds, that anchor wins and standingReach is
            // derived from it, not the other way around — see BuildScript's
            // BuildCharacterModel3D for where that now actually happens
            // (it never did before 2026-08-11, which is why this fallback
            // used to be the only thing that ever ran).
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
            DriveProceduralMotion();
        }

        // ---- input ---------------------------------------------------------
        void HandleInput()
        {
            bool moving = AFLInput.MoveHeld;
            Vector3 dir = Vector3.zero;
            if (moving)
            {
                Vector3 targetPos = moveTarget ? moveTarget.position
                                  : (AFLBall.Instance ? AFLBall.Instance.transform.position : transform.position + transform.forward);
                Vector3 to = targetPos - transform.position; to.y = 0f;
                dir = to.sqrMagnitude > 0.04f ? to.normalized : transform.forward;
            }
            float target = HasBall ? runSpeed * 0.94f : runSpeed;   // carrying costs a touch
            SetMoveIntent(moving ? dir : Vector3.zero, target);

            if (AFLInput.MarkDown) AttemptContest();

            if (HasBall)
            {
                if (AFLInput.KickHeld) _kickCharge = Mathf.Clamp01(_kickCharge + Time.deltaTime / kickChargeTime);
                if (AFLInput.KickUp && _kickCharge > 0.05f) { Kick(_kickCharge, AimDirection()); _kickCharge = 0f; }
            }
            else _kickCharge = 0f;
        }

        // Aim is always the player's own facing, never the camera — issue
        // #1: "kicks aim from camera forward not player facing, camera
        // moves on its own." Facing is already driven by moveTarget (you
        // face whoever/wherever you're curving toward), so this alone also
        // makes kicks land roughly where you were just steering, without a
        // dedicated aim stick.
        Vector3 AimDirection() => transform.forward;

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
            float speed = _horizVel.magnitude;
            animator.SetFloat("Speed", speed, 0.1f, Time.deltaTime);
            animator.SetBool("Airborne", IsAirborne);
            animator.SetBool("HasBall", HasBall);
            animator.SetFloat("KickCharge", _kickCharge);

            // Real fix (2026-08-11, "characters far too static" follow-up):
            // Walk/Run clips previously always played at their fixed baked
            // rate regardless of actual ground speed — a player barely
            // above the Idle->Walk threshold would still cycle a full-pace
            // walk animation, i.e. feet visibly sliding rather than
            // stepping. Scaling playback against whichever locomotion
            // state is actually active keeps the foot-cycle roughly
            // matched to real movement speed. Reference speed comes from
            // this same player's own walkSpeed/runSpeed fields — the exact
            // constants driving movement — not a separately-tuned number.
            var st = animator.GetCurrentAnimatorStateInfo(0);
            float reference = st.IsName("Run") ? runSpeed : walkSpeed;
            animator.speed = speed > 0.3f ? Mathf.Clamp(speed / reference, 0.6f, 1.8f) : 1f;
        }

        // Everything below is cosmetic — it only ever moves the Visual
        // child's local transform, never the CharacterController root, so
        // it can't affect collision, movement or contest logic no matter
        // how it's tuned.
        void DriveProceduralMotion()
        {
            if (!_visual) return;

            // Landing squash: caught on the same grounded transition
            // ApplyMotion() already detects, just observed here a frame
            // later rather than duplicating the check.
            if (_wasAirborne && !IsAirborne) _landedAt = Time.time;
            _wasAirborne = IsAirborne;

            float speed = _horizVel.magnitude;
            float speedT = Mathf.Clamp01(speed / runSpeed);

            // Hop/bob while moving — a kangaroo's run IS a hop, a croc's a
            // waddle, and this one sine drives both well enough without
            // needing per-species branching: height and rate scale with
            // how fast the player is actually moving, so a standing player
            // reads as still, not perpetually bouncing.
            float hopRate = 6.5f + speed * 0.8f;
            float hop = speedT > 0.02f ? Mathf.Abs(Mathf.Sin(Time.time * hopRate)) * speedT * 0.16f : 0f;
            float roll = speedT > 0.02f ? Mathf.Sin(Time.time * hopRate) * speedT * 6f : 0f;   // side-to-side waddle, degrees
            float lean = Mathf.Clamp(speedT * 10f, 0f, 10f);                                    // forward lean into a run

            // Kick lunge: lean back through the charge, snap forward the
            // instant the ball actually leaves (Kick() stamps _kickPulseAt).
            float kickLean = 0f;
            if (HasBall && _kickCharge > 0f) kickLean = -_kickCharge * 14f;    // winding up
            float sinceKick = Time.time - _kickPulseAt;
            if (sinceKick >= 0f && sinceKick < 0.25f)
            {
                float k = 1f - (sinceKick / 0.25f);
                kickLean = 22f * k;                                            // the lunge itself
            }

            // Landing squash-and-stretch, decaying over ~0.18s.
            float sinceLand = Time.time - _landedAt;
            float squash = 0f;
            if (sinceLand >= 0f && sinceLand < 0.18f) squash = (1f - sinceLand / 0.18f) * 0.22f;

            _visual.localPosition = new Vector3(0f, 1f + hop, 0f);
            _visual.localRotation = Quaternion.Euler(lean + kickLean, 0f, roll);
            _visual.localScale = new Vector3(1f + squash * 0.6f, 1f - squash, 1f + squash * 0.6f);
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
                    float idealLead = (needsJump ? TimeToApex : 0.12f) - touchLatencyBias;
                    _timingError = t - idealLead;          // + = too early, - = too late
                    _bidTime = Time.time;
                    _activeBid = true;

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
            _activeBid = false;
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

            // A genuine timed attempt never grades low enough to Spoil —
            // the worst a real jump should do is spill it loose (Fumble),
            // never look like the player punched their own contest away.
            // Issue #1: "a mistimed human press must never resolve as
            // Spoil... the kid punches the ball away themselves."
            if (_activeBid) grade = Mathf.Max(grade, ball.gatherThreshold + 0.02f);

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
            AFLGameManager.Instance?.OnContestSettled(this, grade);
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
            _kickPulseAt = Time.time;
        }

        public void SetControlled(bool on)
        {
            isUserControlled = on;
            if (!on) SetMoveIntent(Vector3.zero, 0f);
        }
    }
}
