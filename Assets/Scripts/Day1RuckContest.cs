using UnityEngine;

namespace AFL.Day1
{
    // =======================================================================
    //  DAY 1 — two rucks at the centre. Nothing else.
    // =======================================================================
    // Per issue #6: a new scene, two players, one button. Does not touch
    // AflField.unity or anything in the six-player game. No movement (the
    // rucks stand still and contest a throw-up), no score, no HUD beyond
    // one message.
    //
    // Correction (2026-08-11): this used to say "no Mixamo clips, no
    // Animator, no Avatar" — there was never any Mixamo in this project to
    // exclude; that line was chasing a tool nobody used. Characters now
    // carry a real Animator (Day1BuildScript) driven by the same Generic
    // controllers the six-player game already ships with, held on Idle for
    // a natural resting pose. The reach/tap itself still has no matching
    // clip to play, so it stays procedural — HopRoutine disables the
    // Animator for its duration so the two don't write the same bones in
    // the same frame, then hands control back.
    public class Day1RuckContest : MonoBehaviour
    {
        public Transform crocVisual;
        public Transform rooVisual;
        // Day 2 (2026-08-11) — rovers are now standing in the scene, but
        // Shaun: "maybe could just leave it a straight tap for now." Not
        // wired into TapBallAway's trajectory yet — that's the next step,
        // deliberately not done in the same pass as just adding the
        // characters.
        public Transform crocRover;
        public Transform rooRover;
        // Day 4 (2026-08-12) — one forward + one defender per team,
        // grouped by contest ZONE not by team: crocForward contests
        // rooDefender near Croc's attacking goal, rooForward contests
        // crocDefender near Roo's. Visual only until now; wiring their
        // movement is this step.
        public Transform crocForward;
        public Transform rooDefender;
        public Transform rooForward;
        public Transform crocDefender;
        public Transform ball;

        public float throwDuration = 2.6f;
        public float peakHeight = 2.1f;
        public float groundY = 1.0f;
        public float hopDuration = 0.45f;
        public float perfectWindow = 0.5f;
        // Real human reaction time to a reactive tap, compensated for in
        // grading — see the comment on ResolveAndContest below.
        public float reactionCompensation = 0.17f;

        float _t;
        bool _humanPressed;
        float _bestHumanErr;
        float _targetT;
        float _botPressT;
        float _hopFireAt;
        float _inputDeadline;
        bool _ballFrozen;
        bool _hopFired;
        bool _resolved;
        bool _sequenceComplete;
        float _resolvedAt;
        string _message = "Centre bounce...";
        GUIStyle _style;

        // Real fix (2026-08-12) — was 2 pairs of named fields (crocRover/
        // rooRover only). Day 4 adds 4 more movers (forward/defender x2);
        // rather than keep adding named field pairs for each one, this
        // generically tracks and resets every movable Transform's start
        // position/rotation, captured once in Start.
        readonly System.Collections.Generic.List<Transform> _movers = new();
        readonly System.Collections.Generic.Dictionary<Transform, (Vector3 pos, Quaternion rot)> _moverStarts = new();

        void Start()
        {
            // Captured once — RunStraight/RunToZ move a player's real
            // position and rotation, so without resetting each round they'd
            // keep drifting further from their starting spot every contest.
            foreach (var mover in new[] { crocRover, rooRover, crocForward, rooDefender, rooForward, crocDefender })
            {
                if (!mover) continue;
                _movers.Add(mover);
                _moverStarts[mover] = (mover.position, mover.rotation);
            }
            BeginThrow();
        }

        void BeginThrow()
        {
            _t = 0f;
            _humanPressed = false;
            _bestHumanErr = float.MaxValue;
            _ballFrozen = false;
            _hopFired = false;
            _resolved = false;
            _sequenceComplete = false;
            foreach (var mover in _movers)
            {
                var (pos, rot) = _moverStarts[mover];
                mover.position = pos;
                mover.rotation = rot;
                var moverAnim = mover.GetComponentInChildren<Animator>();
                if (moverAnim) moverAnim.SetFloat("Speed", 0f);
            }
            _message = "Centre bounce...";
            float ideal = throwDuration * 0.5f;
            _botPressT = ideal + Random.Range(-0.35f, 0.35f);
            // hopFireAt: when the ball freezes at its true visual peak —
            // fixed already (2026-08-11), see git history.
            _hopFireAt = ideal;
            // targetT: the instant a press actually counts as "perfect,"
            // compensated for real human reaction time (see the comment
            // on ResolveAndContest) — not the raw ball peak.
            _targetT = ideal + reactionCompensation;
            // Real fix (2026-08-11, Shaun: "croc has no hope"). The
            // previous version resolved the instant the ball hit its
            // peak — but a human REACTS to seeing the ball at the top,
            // they don't anticipate it, so a normal tap lands slightly
            // AFTER the peak. That's not "too slow," that's how human
            // reflexes work, and the old code silently discarded any
            // press arriving after resolution (see the early-return on
            // _resolved in Update). inputDeadline gives real reaction-time
            // slack past the peak before the contest is forced to resolve
            // without a press — matched to perfectWindow so a press
            // anywhere in that grace period still lands inside the
            // generous auto-win band, not just barely counted.
            _inputDeadline = ideal + perfectWindow;
        }

        void Update()
        {
            if (_resolved)
            {
                // Real fix (2026-08-12) — adding the catch-pause-then-run
                // sequence made the full post-resolve chain (tap flight +
                // catch pause + run) longer than the old fixed 2.2s reset
                // window, which would have reset the scene mid-run. The
                // reset now waits on _sequenceComplete (set at the true end
                // of TapBallAway) rather than a timer that predates the run
                // existing at all.
                if (_sequenceComplete && Time.time - _resolvedAt > 1.2f) BeginThrow();
                return;
            }

            _t += Time.deltaTime;

            // Ball follows the free arc only up until it freezes at the
            // true peak — after that the tap-away coroutine below owns
            // its position. Freezing is now decoupled from resolving (see
            // inputDeadline above): the ball stops rising right at the
            // peak regardless of whether a press has landed yet.
            if (!_ballFrozen)
            {
                float frac = Mathf.Clamp01(_t / throwDuration);
                float height = Mathf.Sin(frac * Mathf.PI) * peakHeight;
                if (ball) ball.position = new Vector3(0f, groundY + height, 0f);
                if (_t >= _hopFireAt) _ballFrozen = true;
            }

            // Real fix (2026-08-12, Shaun: "i just keep hitting tap kid
            // would do that"). This used to grade whichever tap was LAST
            // before resolution — fine for one early miss followed by one
            // considered retry, but a kid mashing continuously has their
            // final, most desperate mash (often landing right at the
            // deadline, far from the peak) count instead of their best
            // one. Now every tap is compared against the target instant
            // and only the closest one so far is kept — mashing helps
            // instead of hurting, matching how a kid actually plays.
            if (Day1Input.TapDown)
            {
                _humanPressed = true;
                float err = Mathf.Abs(_t - _targetT);
                if (err < _bestHumanErr) _bestHumanErr = err;
            }

            // Always resolve at the deadline, using whichever tap was best
            // — no more resolving on the first post-peak press, since that
            // would cut a masher off before their best attempt lands.
            if (!_hopFired && _t >= _inputDeadline)
            {
                _hopFired = true;
                ResolveAndContest();
            }
        }

        // Real fix (2026-08-11, Shaun: "just need the reach up and tap to
        // have a clear winner if the jump is timed correct one person
        // clearly above the other tapping the ball away from the
        // centre"). Both characters used to jump identically regardless
        // of who actually won, so the outcome only ever showed up as
        // text. The winner is now decided at the moment of contest, not
        // at the end of the full throw, and drives three different
        // things: which character's jump reaches full height (the
        // winner) vs a visibly shorter one (the loser), and which
        // direction the ball gets tapped away in afterward.
        // Real fix (2026-08-12, Shaun: "the ai's gone hard to beat in ruck
        // again"). The pendulum swung twice now — generous auto-win made
        // Roo unbeatable, straight comparison then made Roo dominant — and
        // both were tuning the same tolerance number instead of fixing the
        // actual asymmetry. A human's tap is REACTIVE: they perceive the
        // ball at its peak, then physically respond — real human reaction
        // time (commonly ~150-200ms) is baked into every honest press,
        // not carelessness. The bot's error is centered on zero with no
        // such bias. Comparing a systematically-late human distribution
        // against a zero-centered bot distribution unfairly favours the
        // bot even when the human is playing well. Compensating for that
        // known bias directly (rather than re-widening a blanket
        // tolerance again) is the actual fix: a press ~0.17s after the
        // true peak now grades as if it were exactly on time.
        void ResolveAndContest()
        {
            float ideal = throwDuration * 0.5f;
            float humanErr = _humanPressed ? _bestHumanErr : 999f;
            float botErr = Mathf.Abs(_botPressT - ideal);
            bool crocWins = _humanPressed && humanErr <= botErr;

            _message = _humanPressed
                ? (crocWins ? "Crocs win the tap!" : "Roos win the tap!")
                : "Too slow — Roos win the tap!";

            Hop(crocVisual, crocWins);
            Hop(rooVisual, !crocWins);
            StartCoroutine(TapBallAway(crocWins));

            _resolved = true;
            _resolvedAt = Time.time;
        }

        // Real fix (2026-08-11, Shaun: "any chance the ruck could tap the
        // ball to one of the rovers" — the day 2 spec's actual mechanic:
        // "winner of the ruck tap directs the ball to their rover"). This
        // used to fly off in a fixed direction/distance formula with
        // nothing at the far end. Now it targets the winning side's real
        // rover position, so the tap has an actual destination rather
        // than just looking like a knock-away in isolation.
        //
        // Real fix (2026-08-11, same "needs to be clearer who wins" pass)
        // — this used to start the instant the hop fired, so the ball
        // was already flying away while the winner's jump was still
        // rising. Waiting until the winner's hop reaches its own peak,
        // plus a short hold, gives a real "caught it, then tapped it
        // away" beat instead of two things happening at once.
        System.Collections.IEnumerator TapBallAway(bool crocWins)
        {
            yield return new WaitForSeconds(hopDuration / 2f + 0.15f);
            Vector3 start = ball.position;
            Transform rover = crocWins ? crocRover : rooRover;
            // Real fix (2026-08-12, Shaun: "receiving the ball in the back
            // of the head need to receive ball in chest or hands"). The old
            // point sat dead-centre above the rover's own pivot with no
            // forward offset at all — whether that reads as "chest" or
            // "back of the head" was down to luck, not design. The rover's
            // rest facing already points the same way the ball is coming
            // from (it's also the eventual run direction — the rover
            // stands further from goal than both the ruck and the goal
            // itself, so one facing serves catching and running alike), so
            // rover.forward is the correct direction to offset toward.
            // Chest height, not head height.
            //
            // Real fix (2026-08-12, Shaun: "kangaroo get ball in mouth") —
            // the fixed forward/up offset above was tuned by eye against
            // one character (Croc) and didn't generalise: a kangaroo's
            // very different proportions (upright stance, head position)
            // put that same offset near its mouth instead of its chest.
            // Same principle as the run's hand-tracking fix — target the
            // real RightHand bone instead of a guessed body-relative
            // offset, so it adapts to whatever this specific character's
            // actual proportions are rather than needing separate tuning
            // per species.
            var roverHand = rover ? FindDeepChild(rover, "RightHand") : null;
            Vector3 end = roverHand ? roverHand.position : (rover ? rover.position + rover.forward * 0.4f + Vector3.up * 1.1f : start + Vector3.down * 0.6f);
            float dur = 0.6f;
            float el = 0f;
            while (el < dur)
            {
                el += Time.deltaTime;
                float f = Mathf.Clamp01(el / dur);
                // A little arc on the way out so it reads as knocked away,
                // not teleported.
                float arc = Mathf.Sin(f * Mathf.PI) * 0.5f;
                ball.position = Vector3.Lerp(start, end, f) + Vector3.up * arc;
                yield return null;
            }

            // Day 2's placeholder ending (canonical plan): "rover
            // receives it, scene resets" — no catch clip/animation yet,
            // that's explicitly a later day's asset work. A clear message
            // is enough to make the handoff read as one continuous phase
            // rather than the ball just stopping.
            _message = crocWins ? "Crocs' rover gets it!" : "Roos' rover gets it!";

            // Day 3, first slice (2026-08-12, Shaun: "after they receive the
            // ball slight pause then they run... just run straight ahead").
            // A real catch beat before the run starts — receiving and
            // immediately bolting would read as one blurred motion, not two
            // distinct things happening.
            yield return new WaitForSeconds(catchPause);

            // Real fix, same message — run direction is NOT "whichever way
            // the rover happens to be facing" (Shaun: "they face the wrong
            // way, if they run the opposite way to what's set up that's
            // fine"). It's defined directly from the tap: the ball travels
            // from the ruck to the rover, who stands behind their own ruck
            // player, so the run is the mirror of that — straight toward
            // their own goalposts, which is also "opposite the direction
            // the ball was just tapped", per Shaun's own read of it. Croc's
            // rover taps in from -Z, so runs +Z; Roo's is the reverse.
            float runDir = crocWins ? 1f : -1f;
            yield return RunStraight(rover, runDir);

            // Day 3, second slice (2026-08-12, Shaun: "either player takes
            // these few steps then does a kick", "quick pause or just do
            // the kick in that motion" — going with a quick pause, same
            // distinct-beats principle as the catch pause before the run,
            // "kangaroo could just drop the ball on its foot and kick it
            // same as the croc" — one shared mechanic for both, not a
            // per-species animation).
            _message = crocWins ? "Crocs run it out!" : "Roos run it out!";

            // Real fix (2026-08-12, Shaun: "youve kind of gone a bit
            // rouge with this one" — the automatic run-to-landing-spot
            // above wasn't what was actually wanted. "The forward jumps
            // at the ball and trys to take a big jumping catch, timed by
            // catching the ball at the peak of the jump — we have
            // rehersed about this a fair bit." Same jump-timed-to-peak
            // mechanic as the day 1 ruck contest (one button, cue is the
            // ball's own height, generous window, reaction-compensated),
            // just relocated to the kick's flight and applied to the
            // forward vs the opposing defender. Meet the ball at its own
            // PEAK position, not the full landing spot the kick would
            // travel to uncaught — a mark happens in the air, not on the
            // ground, and the kick's Sin arc peaks at exactly the
            // midpoint of its flight, not at the far end.
            // Shaun: "dont worry about the defender yet" — still runs
            // into position (visual presence, matches "one contest zone
            // per end" already built), just no role in grading the catch
            // yet. That's real, separate scope for later.
            Transform forward = crocWins ? crocForward : rooForward;
            Transform defender = crocWins ? rooDefender : crocDefender;
            float peakZ = rover.position.z + runDir * kickDistance * 0.5f;
            float arriveByPeak = kickDropDuration + kickPause + kickDuration * 0.5f;
            StartCoroutine(RunToZ(forward, peakZ, arriveByPeak));
            StartCoroutine(RunToZ(defender, peakZ, arriveByPeak));
            yield return KickAway(rover, runDir, forward);
            // Reset countdown starts from here, not from when the contest
            // first resolved — Update()'s existing reset logic now waits
            // the right amount after the FULL sequence (tap, catch, run,
            // kick) finishes, not from the moment the ruck contest alone
            // resolved. _sequenceComplete is what actually unblocks the
            // reset (see Update) — this timestamp alone doesn't.
            _resolvedAt = Time.time;
            _sequenceComplete = true;
        }

        public float kickPause = 0.3f;
        // Real fix (2026-08-12, Shaun: "the height of the kick in is way
        // to low"). 4 units gave a flat, low arc with no real hang time —
        // not remotely tall enough to read as a genuine long kick a
        // "spectacular mark" could plausibly happen under. Characters are
        // ~2 units tall; a kick meant to be marked needs to arc well
        // above that.
        public float kickHeight = 7f;
        public float kickDistance = 10f;
        public float kickDuration = 1.1f;
        public float kickDropDuration = 0.35f;

        public float markPerfectWindow = 0.5f;
        public float markReactionCompensation = 0.17f;

        // Drop the ball to the foot, brief beat, then kick it away in an
        // arc continuing the same direction as the run. Now interleaved
        // with the mark-timing window (2026-08-12, Shaun: "the forward
        // jumps at the ball and trys to take a big jumping catch, timed
        // by catching the ball at the peak of the jump — we have
        // rehersed about this a fair bit"). Same one-button, cue-is-the-
        // ball's-height mechanic as day 1's ruck contest, not the
        // rejected sub-100ms analytic-physics system — just relocated to
        // the kick's flight. "Dont worry about the defender yet" — this
        // grades the forward's own timing only, no opponent comparison.
        System.Collections.IEnumerator KickAway(Transform t, float zDir, Transform forward)
        {
            if (!t || !ball) yield break;
            var rightHand = FindDeepChild(t, "RightHand");
            var rightFoot = FindDeepChild(t, "RightFoot");
            Vector3 handPos = rightHand ? rightHand.position : ball.position;
            Vector3 footPos = rightFoot ? rightFoot.position + Vector3.up * 0.15f : t.position + Vector3.up * 0.3f;

            // Drop — ball falls from the hand to the foot, a real beat
            // before the kick rather than an instant swap.
            float dropDur = kickDropDuration;
            float el = 0f;
            while (el < dropDur)
            {
                el += Time.deltaTime;
                float f = Mathf.Clamp01(el / dropDur);
                ball.position = Vector3.Lerp(handPos, footPos, f * f); // ease-in, like a real drop under gravity
                yield return null;
            }

            yield return new WaitForSeconds(kickPause);

            // Kick — a real arc continuing the same direction the run was
            // already heading, not a new direction to reason about. The
            // mark window is centred on the arc's own peak (kickDuration
            // * 0.5, the same instant the Sin curve below actually peaks)
            // — the cue IS the ball's height, same principle as day 1.
            Vector3 kickStart = ball.position;
            Vector3 kickEnd = kickStart + new Vector3(0, 0, zDir * kickDistance);
            float peakT = kickDuration * 0.5f;
            float markTargetT = peakT + markReactionCompensation;
            float markDeadline = Mathf.Min(peakT + markPerfectWindow, kickDuration);
            bool markPressed = false;
            float markBestErr = float.MaxValue;
            bool markResolved = false;
            el = 0f;
            while (el < kickDuration)
            {
                el += Time.deltaTime;
                float f = Mathf.Clamp01(el / kickDuration);
                if (!markResolved)
                {
                    float arc = Mathf.Sin(f * Mathf.PI) * kickHeight;
                    ball.position = Vector3.Lerp(kickStart, kickEnd, f) + Vector3.up * arc;
                }

                // Same best-tap-counts pattern as day 1 (Shaun: "i just
                // keep hitting tap kid would do that") — mashing helps,
                // not just the first or last press.
                if (Day1Input.TapDown)
                {
                    markPressed = true;
                    float err = Mathf.Abs(el - markTargetT);
                    if (err < markBestErr) markBestErr = err;
                }

                if (!markResolved && el >= markDeadline)
                {
                    markResolved = true;
                    bool marked = markPressed && markBestErr <= markPerfectWindow;
                    ResolveMark(forward, marked);
                }
                yield return null;
            }
        }

        // Real fix (2026-08-12, Shaun: "with a mark the forward catches
        // the ball"). Reuses HopRoutine as-is (the exact same reach
        // animation the ruck contest already uses) rather than a new
        // jump routine — a marked ball ends up genuinely held in the
        // forward's hand, tracked live the same way the run/catch
        // elsewhere in this file already does, not just a text message.
        void ResolveMark(Transform forward, bool marked)
        {
            _message = marked ? "MARK!" : "Spilled!";
            if (!forward) return;
            StartCoroutine(MarkCatchRoutine(forward, marked));
        }

        System.Collections.IEnumerator MarkCatchRoutine(Transform forward, bool marked)
        {
            Hop(forward, marked);
            if (!marked) yield break;
            // Ball snaps to the forward's actual raised hand once their
            // jump reaches its own peak (same hopDuration/2 timing
            // HopRoutine itself peaks on), then keeps tracking it live —
            // same principle as why the run's ball-on-a-fixed-offset
            // looked wrong earlier tonight (2026-08-12, "balancing the
            // ball on there tummy"): track the real bone, not a guess.
            yield return new WaitForSeconds(hopDuration / 2f);
            var hand = FindDeepChild(forward, "RightHand");
            float holdEl = 0f;
            while (holdEl < 1f)
            {
                holdEl += Time.deltaTime;
                if (hand && ball) ball.position = hand.position;
                yield return null;
            }
        }

        public float catchPause = 0.5f;
        // Real fix (2026-08-12, Shaun: "run of a bit far", then "can be a
        // slower like 4 step run"). 14 units / 1.8s was a full sprint pace
        // with no acceleration — cut down to a short, deliberate few-step
        // jog instead. runSpeed on AFLPlayer (the six-player game's own
        // player script) calibrates its Run animation state at 7 units/sec
        // and Walk at half that (3.5) — this run now moves at ~4 units/sec,
        // matching Walk-ish pace rather than a full Run blend.
        public float runDistance = 6f;
        public float runDuration = 1.5f;

        // Day 4, first slice (2026-08-12, Shaun: "what kind of happens
        // when the ball is kicked into the forward line"). Straight down
        // the Z axis only, own X unchanged — same control rule as
        // everywhere else in this game, no steering. Timed to a fixed
        // duration (the kick's own drop+pause+flight time) rather than a
        // fixed distance, since the forward and defender don't start the
        // same distance from the landing point every time.
        System.Collections.IEnumerator RunToZ(Transform t, float targetZ, float duration)
        {
            if (!t) yield break;
            var animator = t.GetComponentInChildren<Animator>();
            Vector3 start = t.position;
            Vector3 end = new Vector3(start.x, start.y, targetZ);
            t.rotation = Quaternion.Euler(0, targetZ > start.z ? 0 : 180, 0);
            float el = 0f;
            while (el < duration)
            {
                el += Time.deltaTime;
                float f = Mathf.Clamp01(el / duration);
                float smoothF = Mathf.SmoothStep(0f, 1f, f);
                t.position = Vector3.Lerp(start, end, smoothF);
                if (animator)
                {
                    float rampF = Mathf.Clamp01(Mathf.Min(f, 1f - f) / 0.15f);
                    animator.SetFloat("Speed", 5.5f * rampF, 0.1f, Time.deltaTime);
                }
                yield return null;
            }
            t.position = end;
            if (animator) animator.SetFloat("Speed", 0f, 0.1f, Time.deltaTime);
        }

        // Straight-line only, per the canonical plan's control rule — no
        // steering, the ball/handoff always puts the player in the right
        // lane so a straight run is always correct. No kick yet (that's
        // the next slice); this ends on an honest placeholder message, not
        // a stall into not-yet-built work.
        System.Collections.IEnumerator RunStraight(Transform t, float zDir)
        {
            if (!t) yield break;
            var animator = t.GetComponentInChildren<Animator>();
            t.rotation = Quaternion.Euler(0, zDir > 0 ? 0 : 180, 0);

            // Real fix (2026-08-12, Shaun: "looks like they are balancing
            // the ball on there tummy"). The ball used to follow a fixed
            // offset from the rover's ROOT transform — completely
            // detached from where the hands actually are, so it could
            // never look held, only carried on an invisible shelf. Track
            // the real LeftHand/RightHand bones instead (same
            // FindDeepChild pattern HopRoutine already uses for arms), so
            // the ball genuinely follows whatever the hands are doing.
            var leftHand = FindDeepChild(t, "LeftHand");
            var rightHand = FindDeepChild(t, "RightHand");

            Vector3 start = t.position;
            Vector3 end = start + new Vector3(0, 0, zDir * runDistance);
            // Real fix (2026-08-12, Shaun: "same problem" after the hand-
            // tracking fix — the ball attachment was correct, but nothing
            // was actually reaching the hands to attach to. Read directly
            // from CrocRiggedAIAnimator.controller: Idle only transitions
            // to Walk once Speed exceeds ~4.55. My previous velocity curve
            // peaked at ~6 for one instant at the midpoint and spent most
            // of the run below that threshold — the character was in Idle
            // (still arms, ball parked near the idle hand position — which
            // reads exactly as "balancing on the tummy") for nearly the
            // whole run. The animator's Speed parameter doesn't have to
            // match the actual translation speed — AFLPlayer only does
            // that because its movement pace happens to already sit above
            // this same threshold. Here, decoupled on purpose: physical
            // movement stays at the slower "4 step jog" pace Shaun asked
            // for, while the animator gets a sustained value comfortably
            // over 4.55 so it actually reaches and holds Walk instead of
            // brushing past the threshold for one frame.
            const float animSpeed = 5.5f;
            float el = 0f;
            while (el < runDuration)
            {
                el += Time.deltaTime;
                float f = Mathf.Clamp01(el / runDuration);
                // Real fix (2026-08-12, "strange looking run" report) —
                // this used to move at constant speed with an instant
                // start/stop. SmoothStep gives a real accelerate-then-
                // decelerate arc for the physical movement instead.
                float smoothF = Mathf.SmoothStep(0f, 1f, f);
                t.position = Vector3.Lerp(start, end, smoothF);
                if (ball)
                {
                    // Real fix (2026-08-12, Shaun: "arms moving when it
                    // runs just ball in middle of its chest still"). The
                    // midpoint of both hands was the bug — a normal run
                    // cycle swings the hands in OPPOSITE phase (left
                    // forward as right swings back), so their midpoint
                    // mostly cancels out and barely moves even though each
                    // hand individually swings a lot. Tracking one hand
                    // (tucked-under-the-arm carry, not a two-handed cradle)
                    // actually shows the motion instead of averaging it away.
                    if (rightHand) ball.position = rightHand.position;
                    else if (leftHand) ball.position = leftHand.position;
                    else ball.position = t.position + Vector3.up * 1.1f;
                }
                if (animator)
                {
                    // Trapezoid: quick ramp up/down at the very ends (still
                    // damped besides), sustained comfortably above the
                    // 4.55 Walk threshold for the middle of the run so the
                    // transition actually completes and holds, not a
                    // triangular peak that only grazes it.
                    float rampF = Mathf.Clamp01(Mathf.Min(f, 1f - f) / 0.15f);
                    animator.SetFloat("Speed", animSpeed * rampF, 0.1f, Time.deltaTime);
                }
                yield return null;
            }
            t.position = end;
            if (ball)
            {
                if (rightHand) ball.position = rightHand.position;
                else if (leftHand) ball.position = leftHand.position;
                else ball.position = end + Vector3.up * 1.1f;
            }
            if (animator) animator.SetFloat("Speed", 0f, 0.1f, Time.deltaTime);
        }

        // Explicitly rough, explicitly not a real animation — issue #6:
        // "if the leap looks rough today, that is fine." Straight transform
        // lerp via a coroutine, no Animator, no clip.
        //
        // The winner reaches full height and genuinely closes the gap to
        // where the ball was (see the reach-distance fix below); the
        // loser's jump is deliberately shorter (0.55x height, no
        // lean-toward-centre) so the two are visibly different — one
        // clearly above the other, per Shaun's direct request.
        void Hop(Transform t, bool reachesBall)
        {
            if (t) StartCoroutine(HopRoutine(t, reachesBall));
        }

        static Transform FindDeepChild(Transform parent, string name)
        {
            foreach (var child in parent.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }

        // Reach math (measured directly, not eyeballed — see this file's
        // git history for the actual hand-position measurements this is
        // based on): standing with arms raised 140° on the rig's Z axis,
        // hands sit at world y≈1.48, barely moved in X from the
        // character's own stance. Ball peaks at groundY + peakHeight =
        // 1.0 + 2.1 = 3.1. A full 1.5-unit hop brings hand height to
        // ≈2.98 — genuine contact.
        //
        // Real fix (2026-08-11, Shaun: "higher arm extension would be
        // excellent" then "need them showing it clearly being tapped").
        // A direct render+measure sweep (ArmAngleCheck.cs, 100-165 degrees)
        // showed hand HEIGHT is flat across that whole range — the reach
        // shortfall against the ball isn't an angle problem, it's the
        // hop's own vertical scale (fixed below via heightScale). Angle
        // itself moved from 140 to 155 for the visual read Shaun asked
        // for — confirmed by the same sweep to cost nothing, since height
        // doesn't drop off in that range either.
        //
        // Real fix (2026-08-11, Shaun: "sorry that needs to be clearer
        // who wins", then "if croc barely moved thats not good") — two
        // adjustments in a row that needed balancing against each other.
        // First pass (0.85x/70°) was too close to call at a glance.
        // Second pass (0.2x/20°) over-corrected — a character that barely
        // moves reads as unresponsive/broken, not as "lost the contest,"
        // especially when it's the player's own side. Settled on a real,
        // visible jump attempt (0.5x height, 60° arms — about a third of
        // the winner's height) that's unambiguous either way: clearly a
        // genuine try, clearly not as high as the winner's.
        System.Collections.IEnumerator HopRoutine(Transform t, bool reachesBall)
        {
            Vector3 start = t.localPosition;
            // Real fix (2026-08-11, Shaun: "need them showing it clearly
            // being tapped"). Swept hand height across 100-165 degrees via
            // a direct render+measure check (ArmAngleCheck.cs) rather than
            // guessing further — it plateaus at ~2.95-2.98 the whole range,
            // meaning the arm was already near the top of its physical
            // reach at 140 degrees and more angle wasn't the lever. The
            // actual shortfall against the ball's frozen height (3.1) is
            // the hop's own vertical scale, not the arm — bumped 1.5 to
            // 1.65 to close that ~0.13-unit gap so the hand and ball
            // actually meet instead of falling just short.
            float heightScale = reachesBall ? 1.65f : 0.5f;
            Vector3 towardCentre = reachesBall
                ? new Vector3(-Mathf.Sign(start.x) * 0.55f, 0, 0)
                : Vector3.zero;
            float armAngle = reachesBall ? 155f : 60f;

            var leftArm = FindDeepChild(t, "LeftArm");
            var rightArm = FindDeepChild(t, "RightArm");
            Quaternion leftStart = leftArm ? leftArm.localRotation : Quaternion.identity;
            Quaternion rightStart = rightArm ? rightArm.localRotation : Quaternion.identity;

            // Real fix (2026-08-11) — the character now carries a real
            // Animator (Idle state, see Day1BuildScript) so it doesn't sit
            // frozen in the raw import bind pose between contests. Mecanim
            // writes bone transforms in its own update pass regardless of
            // what this coroutine does, so left running during the hop it
            // would silently overwrite these LeftArm/RightArm rotations the
            // moment they're set. Switch it off for the hop's duration,
            // back on once the pose is handed back to Idle.
            var animator = t.GetComponentInChildren<Animator>();
            if (animator) animator.enabled = false;

            float dur = hopDuration;
            float el = 0f;
            while (el < dur)
            {
                el += Time.deltaTime;
                float f = el / dur;
                float wave = Mathf.Sin(f * Mathf.PI);   // 0 -> 1 -> 0
                t.localPosition = start + Vector3.up * wave * heightScale + towardCentre * wave;
                if (leftArm) leftArm.localRotation = leftStart * Quaternion.Euler(0, 0, wave * armAngle);
                if (rightArm) rightArm.localRotation = rightStart * Quaternion.Euler(0, 0, -wave * armAngle);
                yield return null;
            }
            t.localPosition = start;
            if (leftArm) leftArm.localRotation = leftStart;
            if (rightArm) rightArm.localRotation = rightStart;
            if (animator) animator.enabled = true;
        }

        // One message, large, high contrast — same legibility bar the rest
        // of this project's rewrite already established.
        void OnGUI()
        {
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.RoundToInt(Screen.height * 0.06f),
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.white }
                };
            }
            int panelH = Mathf.RoundToInt(Screen.height * 0.14f);
            int y = Mathf.RoundToInt(Screen.height * 0.08f);
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(new Rect(0, y, Screen.width, panelH), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(0, y, Screen.width, panelH), _message, _style);
        }
    }

    // Minimal input wrapper, deliberately separate from AFLInput (the
    // six-player game's input) — one button only, per issue #6.
    public static class Day1Input
    {
        public static bool TouchTapDown;
        public static bool TapDown => Input.GetKeyDown(KeyCode.Space) || TouchTapDown;
        internal static void ClearOneShot() { TouchTapDown = false; }
    }

    public class Day1TouchBridge : MonoBehaviour
    {
        void LateUpdate() { Day1Input.ClearOneShot(); }
        public void TapPressed(string _) { Day1Input.TouchTapDown = true; }
    }
}
