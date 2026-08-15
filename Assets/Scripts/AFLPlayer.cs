using System.Collections.Generic;
using UnityEngine;

namespace AFL
{
    // =======================================================================
    //  PLAYER  (user controlled or AI driven)
    // =======================================================================
    // Rewritten 2026-08-11 for the beat-based redesign (issue #1, "beat
    // rewrite" comment). The player no longer steers toward a moveTarget —
    // movement is forward-only, in a fixed direction set once per beat by
    // AFLGameManager, never re-aimed by the player. This is a deliberate
    // simplification, not a missing feature: free chase-anywhere movement
    // was the original design and it failed (players could never reliably
    // reach a ball that could be anywhere). In the beat system the ball is
    // always delivered to wherever the active player already is, so there
    // is nothing left to steer toward. See AFLBeatPrompt for how contest
    // timing/kick aim now works — none of that math lives here any more.
    [AddComponentMenu("AFL/AFL Player")]
    [RequireComponent(typeof(CharacterController))]
    public class AFLPlayer : MonoBehaviour
    {
        public enum Team { Home, Away }
        public static readonly List<AFLPlayer> All = new List<AFLPlayer>();

        [Header("Identity")]
        public Team team = Team.Home;
        public bool isUserControlled;
        public bool isRuck;
        public Animator animator;
        public Transform ballHold;
        public Transform handsAnchor;

        [Header("Movement — forward-only, fixed direction")]
        public float runSpeed = 7.0f;
        public float acceleration = 24f;
        public float gravity = -14f;
        public float jumpHeight = 1.15f;

        // runtime
        CharacterController _cc;
        Vector3 _horizVel;
        float _vertVel;
        bool _moveHeld;

        Transform _visual;
        bool _wasAirborne;
        float _landedAt = -99f;
        float _kickPulseAt = -99f;
        float _kickChargeVisual;   // purely cosmetic lean-back while a kick beat is live
        float _spoiledAt = -99f;  // purely cosmetic swipe while a defender spoils a mark

        public bool IsAirborne { get; private set; }
        public bool HasBall => AFLBall.Instance && AFLBall.Instance.Carrier == this;
        public Vector3 Velocity => _horizVel;
        float JumpVelocity => Mathf.Sqrt(2f * jumpHeight * -gravity);

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _visual = transform.Find("Visual");
            if (!ballHold) ballHold = CreateAnchor("BallHold", new Vector3(0.35f, 1.15f, 0.35f));
            if (!handsAnchor) handsAnchor = CreateAnchor("Hands", new Vector3(0f, 2.2f, 0.25f));
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
            if (isUserControlled) _moveHeld = AFLInput.MoveHeld;
            ApplyMotion();
            DriveAnimator();
            DriveProceduralMotion();
        }

        // Bots drive this directly instead of going through AFLInput —
        // see AFLBotBrain, which sets it every frame based on which beat is
        // live rather than reading a shared input flag meant for the human.
        public void SetMoveHeld(bool held) { _moveHeld = held; }

        void ApplyMotion()
        {
            // Fixed direction — transform.forward, set once at spawn/beat-
            // start and never re-aimed by movement itself. No turning
            // input exists; see class comment.
            Vector3 desired = _moveHeld ? transform.forward * runSpeed : Vector3.zero;
            _horizVel = Vector3.MoveTowards(_horizVel, desired, acceleration * Time.deltaTime);

            if (_cc.isGrounded && _vertVel < 0f) { _vertVel = -2f; IsAirborne = false; }
            _vertVel += gravity * Time.deltaTime;

            _cc.Move((_horizVel + Vector3.up * _vertVel) * Time.deltaTime);
        }

        /// Called by AFLGameManager at the start of a beat — the only place
        /// facing ever changes now. No smoothing: a cut, not a turn.
        public void SnapFacing(Vector3 forward)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f) return;
            transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        /// Called by AFLGameManager when a mark-contest beat resolves in
        /// this player's favour and needed a jump (ball arrives above
        /// standing reach). Purely a visual/physical hop — the actual
        /// catch/spoil grading already happened via AFLBeatPrompt.
        public void Jump()
        {
            if (!_cc.isGrounded) return;
            _vertVel = JumpVelocity;
            IsAirborne = true;
            if (animator) animator.SetTrigger("Jump");
        }

        /// Called by AFLGameManager when a defender wins a mark contest by
        /// spoiling rather than catching. Reuses Jump()'s rig/animation
        /// state (no new Animator parameter needed — this project's real
        /// motion read comes from DriveProceduralMotion() below, not the
        /// underlying clip, same as the kick lean and landing squash) but
        /// a shorter, lower hop than a clean mark: a defender is swatting
        /// the ball away, not rising to gather it cleanly. The distinct
        /// read comes from the one-armed swipe layered on in
        /// DriveProceduralMotion(), not from a separate jump height.
        public void Spoil()
        {
            if (!_cc.isGrounded) return;
            _vertVel = JumpVelocity * 0.7f;
            IsAirborne = true;
            _spoiledAt = Time.time;
            if (animator) animator.SetTrigger("Jump");
        }

        void DriveAnimator()
        {
            if (!animator) return;
            float speed = _horizVel.magnitude;
            animator.SetFloat("Speed", speed, 0.1f, Time.deltaTime);
            animator.SetBool("Airborne", IsAirborne);
            animator.SetBool("HasBall", HasBall);
            animator.SetFloat("KickCharge", _kickChargeVisual);

            var st = animator.GetCurrentAnimatorStateInfo(0);
            float reference = st.IsName("Run") ? runSpeed : runSpeed * 0.5f;
            animator.speed = speed > 0.3f ? Mathf.Clamp(speed / reference, 0.6f, 1.8f) : 1f;
        }

        // Everything below is cosmetic — only ever moves the Visual child's
        // local transform, never the CharacterController root.
        void DriveProceduralMotion()
        {
            if (!_visual) return;

            if (_wasAirborne && !IsAirborne) _landedAt = Time.time;
            _wasAirborne = IsAirborne;

            float speed = _horizVel.magnitude;
            float speedT = Mathf.Clamp01(speed / runSpeed);

            float hopRate = 6.5f + speed * 0.8f;
            float hop = speedT > 0.02f ? Mathf.Abs(Mathf.Sin(Time.time * hopRate)) * speedT * 0.16f : 0f;
            float roll = speedT > 0.02f ? Mathf.Sin(Time.time * hopRate) * speedT * 6f : 0f;
            float lean = Mathf.Clamp(speedT * 10f, 0f, 10f);

            float kickLean = HasBall ? -_kickChargeVisual * 14f : 0f;
            float sinceKick = Time.time - _kickPulseAt;
            if (sinceKick >= 0f && sinceKick < 0.25f)
                kickLean = 22f * (1f - sinceKick / 0.25f);

            float sinceLand = Time.time - _landedAt;
            float squash = (sinceLand >= 0f && sinceLand < 0.18f) ? (1f - sinceLand / 0.18f) * 0.22f : 0f;

            // One-armed swipe read: a fast yaw snap across the body (as if
            // swatting the ball sideways) plus an aggressive forward lurch,
            // envelope rises quick and falls slower — smooth-in/sharp-read,
            // distinct from Jump()'s plain vertical rise. Duration (0.32s)
            // sits comfortably above this game's 0.25s minimum timing floor
            // (see CLAUDE.md "recurring failure" section) since it's purely
            // cosmetic and never gates input.
            const float spoilDur = 0.32f;
            float sinceSpoil = Time.time - _spoiledAt;
            float spoilT = (sinceSpoil >= 0f && sinceSpoil < spoilDur) ? sinceSpoil / spoilDur : -1f;
            float spoilEnv = spoilT >= 0f ? Mathf.Sin(spoilT * Mathf.PI) : 0f;   // 0 -> 1 -> 0
            float spoilYaw = spoilEnv * 32f;
            float spoilLurch = spoilEnv * 16f;

            _visual.localPosition = new Vector3(0f, 1f + hop, spoilEnv * 0.12f);
            _visual.localRotation = Quaternion.Euler(lean + kickLean + spoilLurch, spoilYaw, roll);
            _visual.localScale = new Vector3(1f + squash * 0.6f, 1f - squash, 1f + squash * 0.6f);
        }

        // ---- disposal ---------------------------------------------------
        // Aim/power now always supplied explicitly by whoever resolved the
        // beat (AFLGameManager, from AFLBeatPrompt's graded value) — never
        // derived from facing. See issue #1 section 4: "with forward-only
        // movement, aim can no longer come from the character's facing."
        public void Kick(float power, Vector3 flatDir)
        {
            var ball = AFLBall.Instance;
            if (!ball || ball.Carrier != this) return;

            power = Mathf.Clamp01(power);
            float speed = Mathf.Lerp(10f, 17.5f, power);
            float angle = Mathf.Lerp(25f, 38f, power);
            Vector3 dir = Quaternion.AngleAxis(-angle, Vector3.Cross(Vector3.up, flatDir)) * flatDir;

            ball.Release(dir.normalized * speed, transform.right * -18f, this, true);
            if (animator) animator.SetTrigger("Kick");
            _kickPulseAt = Time.time;
            _kickChargeVisual = 0f;
        }

        /// Cosmetic only — AFLGameManager calls this while a kick-beat's
        /// arrow is live so the wind-up lean reads correctly; it has no
        /// effect on the actual kick outcome (that's graded by
        /// AFLBeatPrompt, not by how long this has been held).
        public void SetKickChargeVisual(float v01) { _kickChargeVisual = Mathf.Clamp01(v01); }
    }
}
