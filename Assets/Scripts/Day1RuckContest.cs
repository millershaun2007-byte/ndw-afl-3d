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
        // 2026-08-19: dedicated receivers for the forward line, same role
        // as crocRover/rooRover for the centre — Mia/Summer, not reused
        // forward/defender characters (see MainBuildScript's setup for
        // why: "add 2 more characters so it does work like the centre").
        public Transform rooClearer;
        // 2026-08-28, Shaun: "need correct amount of players even if you add more
        // would make sense to add 2 more". Dedicated runners, one per side, so
        // the handball goes to a real spare rather than borrowing the clearer.
        public Transform crocRunner;
        public Transform rooRunner;
        public Transform crocClearer;
        public Transform ball;

        // Real fix (2026-08-12, Shaun: "still cant see both teams goals").
        // The fixed camera sits between the two goals (z +-20) facing one
        // direction, so the far goal can never appear in it at any
        // pullback distance without permanently sacrificing the close,
        // already-playtested framing (see the reverted v571 pullback).
        // The old canceled game's own camera never solved this with one
        // static shot either -- AFLBroadcastCamera.CutToSide() switches to
        // a side-on framing specifically for the kick/mark beat, "where
        // seeing both the player and the ball's flight matters more than
        // the usual angle" (its own header comment). Reused here: cut wide
        // and side-on only for the kick's flight, then cut back once the
        // mark resolves.
        Camera _mainCam;
        Coroutine _defenderRunToZ;
        Vector3 _camDefaultPos;
        Quaternion _camDefaultRot;
        // Must match Day1BuildScript's BuildGoalPosts z position.
        public float goalZ = 20f;
        public float kickCamSide = 16f;
        public float kickCamHeight = 8f;
        // Real fix (2026-08-12, Shaun: "maybe pause them in mid air when
        // they have taken the mark" / "just brief pause"). Held once the
        // mark-jump's rise reaches its peak, only when it's a genuine
        // mark — a spill falls straight back down instead.
        public float markCelebrationHold = 0.3f;
        bool _markHoldReleased;
        bool _markHoldSucceeded;

        public float throwDuration = 2.6f;
        public float peakHeight = 2.1f;
        public float groundY = 1.0f;
        public float hopDuration = 0.45f;
        // 2026-08-28: ONE height and ONE arm angle for any leap that contests
        // the ball, so the forward and the defender actually meet.
        public float contestLeapHeight = 1.65f;
        public float contestArmAngle = 155f;
        public float spoilPunchHeight = 0.8f;     // a deflection, not a kick
        public float spoilPunchDuration = 0.45f;  // sharp, roughly the hop
        // 2026-08-19: rise time for NormalMarkHop, the mark-specific hop
        // that holds at peak until the outcome is known rather than
        // landing on a fixed clock (see NormalMarkHop's own header for
        // why the old plain Hop() couldn't be reused as-is here).
        public float normalMarkHopRiseDuration = 0.3f;
        // 2026-08-19: guaranteed minimum time NormalMarkHop/SpeccyLeap
        // hold at their peak pose once released, so a frame-timing hitch
        // can't collapse the hold to ~0 duration and drop straight into
        // the fall before the peak was ever visibly held.
        public float minMarkHoldDuration = 0.2f;
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
        // How long a passage may sit without completing before it is force-restarted.
        public float stallTimeout = 6f;
        float _resolvedAt;
        string _message = "Centre bounce...";
        GUIStyle _style;
        // Real fix (2026-08-12) — found via a live Playwright check, not
        // guessed: the mark's 1s ball-hold loop and the round-reset timer
        // (1.2s after the full sequence) land almost exactly coincident,
        // so a stray hold-loop from the PREVIOUS round could still be
        // overwriting the ball's position after BeginThrow already reset
        // it for the new one — confirmed via screenshot, ball sitting up
        // near the old goal during a fresh "Centre bounce..." round.
        // Incremented every BeginThrow; any coroutine spanning a reset
        // checks this and bails rather than relying on timing coincidences
        // to always land the right way.
        int _roundId;

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
            foreach (var mover in new[] { crocRover, rooRover, crocForward, rooDefender, rooForward, crocDefender, rooClearer, crocClearer })
            {
                if (!mover) continue;
                _movers.Add(mover);
                _moverStarts[mover] = (mover.position, mover.rotation);
            }
            _mainCam = Camera.main;
            if (_mainCam)
            {
                _camDefaultPos = _mainCam.transform.position;
                _camDefaultRot = _mainCam.transform.rotation;
            }
            BeginThrow();
        }

        void BeginThrow()
        {
            _roundId++;
            _t = 0f;
            _humanPressed = false;
            _bestHumanErr = float.MaxValue;
            _ballFrozen = false;
            _hopFired = false;
            _resolved = false;
            _sequenceComplete = false;
            _steerOffsetX = 0f;
            foreach (var mover in _movers)
            {
                var (pos, rot) = _moverStarts[mover];
                mover.position = pos;
                mover.rotation = rot;
                var moverAnim = mover.GetComponentInChildren<Animator>();
                if (moverAnim) moverAnim.SetFloat("Speed", 0f);
            }
            // Belt and braces, same reasoning as the mover reset above: a
            // fresh round always starts on the default close camera,
            // regardless of whether the previous round's kick-cut ever
            // got a chance to cut itself back (e.g. the human resetting
            // mid-hold).
            if (_mainCam)
            {
                _mainCam.transform.position = _camDefaultPos;
                _mainCam.transform.rotation = _camDefaultRot;
            }
            _message = "Centre bounce...";
            float ideal = throwDuration * 0.5f;
            // 2026-08-19, Shaun: "too easy for the crocs" — the human
            // plays Croc here and gets two structural advantages this bot
            // never had: reactionCompensation (below) shifts their target
            // in their favour, AND mashing means only their SINGLE best
            // tap out of many counts, vs the bot's one uncorrelated random
            // guess. Narrowing the bot's spread doesn't erase either
            // advantage but makes Roo a real, competitive opponent instead
            // of pure noise.
            _botPressT = ideal + Random.Range(-0.15f, 0.15f);
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
                if (_sequenceComplete && Time.time - _resolvedAt > 1.2f) { BeginThrow(); return; }
                // 2026-08-28, Shaun: "after of target and it sits near the boundary
                // line its still frozen".
                //
                // A passage that never sets _sequenceComplete leaves the match
                // stopped forever, and there is no recovery from it - the player
                // just watches everyone stand still. Every exit in TapBallAway
                // does set it, so a survivor here means a coroutine died partway
                // (a real exception, or a beat that returned by a route not yet
                // found). Rather than leave the game bricked, restart the passage
                // and say so, which also names the beat it happened in.
                if (!_sequenceComplete && Time.time - _resolvedAt > stallTimeout)
                {
                    Debug.LogWarning("[stall] sequence never completed after \"" + _message
                        + "\" - restarting the passage");
                    _sequenceComplete = true;
                }
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
        // 2026-08-19, Shaun: "the other defender receives the ball after
        // the spoil, pause, then they kick — exactly the same as what
        // happens in the middle." kickerOverride lets the clearance reuse
        // this whole receive/pause/run/kick sequence unchanged but with
        // the reinforcement (the idle Lion/Dragon) standing in for the
        // rover, instead of the ball going to a third, unrelated
        // character. Null (the centre ruck's own call) keeps the normal
        // rover behaviour exactly as it was.
        // 2026-08-21 — real bug found by re-deriving the actual numbers,
        // not guessing at the symptom again ("all over the shop" / "the
        // defence... works it up towards the other goals"). A team's
        // attacking direction is fixed for the whole game — Crocs always
        // +Z, Roos always −Z — it's a property of the TEAM, not of
        // possession. The old reverseDirection parameter flipped runDir
        // a SECOND time, independently of the crocWins flip that already
        // happens when possession changes hands — the kick-out's own
        // chain call (crocWins: !humanControlled, reverseDirection: true)
        // had both flips at once, which cancel: possession changes teams
        // but the direction stays exactly what it was, so the recovering
        // team ends up running toward the goal they were just defending
        // instead of their own. Removed entirely — a team's direction
        // now derives from nothing but which team it is.
        //
        // Renamed crocWins -> crocsInPossession in this function
        // specifically: it stopped meaning "won the tap" the moment this
        // became re-entrant (called again mid-round with the ball
        // already loose) — it now means "which team currently has it,"
        // ongoing state rather than a one-off event. The old name
        // describing an event, tracking state that changes over a
        // round, is exactly how the direction bug above went unnoticed.
        //
        // 2026-08-21 — chainDepth added for the second contest after a
        // kick-out (see SECOND-CONTEST-BRIEF.md). This method was already
        // re-entrant (the clearance path already reused it), so the
        // second contest is just another call — but a spoil at that
        // point would otherwise chain a THIRD kick-out, then a fourth
        // contest, without limit. chainDepth stops that at the first
        // level: only chainDepth==0 (the original centre-bounce contest)
        // is allowed to chain into a kick-out's own second contest; any
        // deeper spoil ends the round cleanly instead (see KickAway's
        // defenderSpoiled branch).
        System.Collections.IEnumerator TapBallAway(bool crocsInPossession, Transform kickerOverride = null, int chainDepth = 0)
        {
            yield return new WaitForSeconds(hopDuration / 2f + 0.15f);
            Vector3 start = ball.position;
            Transform rover = kickerOverride ? kickerOverride : (crocsInPossession ? crocRover : rooRover);
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
            _message = crocsInPossession ? "Crocs' rover gets it!" : "Roos' rover gets it!";

            // Day 3, first slice (2026-08-12, Shaun: "after they receive the
            // ball slight pause then they run... just run straight ahead").
            // A real catch beat before the run starts — receiving and
            // immediately bolting would read as one blurred motion, not two
            // distinct things happening.
            yield return new WaitForSeconds(catchPause);

            // Real fix, same message — run direction is NOT "whichever way
            // the rover happens to be facing" (Shaun: "they face the wrong
            // way, if they run the opposite way to what's set up that's
            // fine"). Croc always attacks +Z, Roo always -Z — fixed for
            // the whole game, a property of the TEAM, not of possession
            // or of how this particular call got here (see this
            // function's own header comment on the reverseDirection bug
            // this replaced).
            float runDir = crocsInPossession ? 1f : -1f;
            // Centre clearance only - not a kick-out or a chain hop, which have
            // their own shape. kickerOverride is null exactly when this came
            // from the ruck.
            if (chainDepth == 0 && !kickerOverride)
            {
                float roll = Random.value;
                if (roll < centreBurstChance)
                {
                    yield return BurstBounceSnap(rover, runDir, crocsInPossession);
                    // 2026-08-28, Shaun: "of target and the players just stand still".
                    // These two scenes returned straight out of TapBallAway, skipping
                    // the _sequenceComplete assignment at the end of it - and Update()
                    // only starts the next centre bounce once that is set. So after a
                    // snap the match simply stopped, forever. Both paths now end the
                    // sequence the same way the normal one does.
                    _resolvedAt = Time.time;
                    _sequenceComplete = true;
                    yield break;
                }
                if (roll < centreBurstChance + handballChance)
                {
                    yield return HandballAndRun(rover, runDir, crocsInPossession);
                    _resolvedAt = Time.time;
                    _sequenceComplete = true;
                    yield break;
                }
            }
            yield return RunStraight(rover, runDir, steerable: crocsInPossession);
            // 2026-08-21 — real bug, found by computing the actual
            // numbers rather than guessing again: every chain hop
            // (kick-out's second contest, an out-of-range mark, a spoil
            // past the first contest) advances the rover by runDistance
            // (6) here, then peakZ below adds another kickDistance*0.5
            // (8) — 14 units further downfield per hop, with no bounds
            // check. The kick-out itself already starts the chain
            // partway to a goal (kickOutTargetZ), so after just 1-2 hops
            // this can already exceed the field's own real half-length
            // (goalZ=20) — confirmed as the actual cause of the camera
            // ending up pointed at the sky in live testing (the pivot
            // landed outside the field entirely, not a ball-height or
            // stale-camera issue as first guessed). Clamp to stay on the
            // actual ground regardless of chain depth.
            rover.position = new Vector3(rover.position.x, rover.position.y, Mathf.Clamp(rover.position.z, -(goalZ - 2f), goalZ - 2f));

            // Day 3, second slice (2026-08-12, Shaun: "either player takes
            // these few steps then does a kick", "quick pause or just do
            // the kick in that motion" — going with a quick pause, same
            // distinct-beats principle as the catch pause before the run,
            // "kangaroo could just drop the ball on its foot and kick it
            // same as the croc" — one shared mechanic for both, not a
            // per-species animation).
            _message = crocsInPossession ? "Crocs run it out!" : "Roos run it out!";

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
            Transform forward = crocsInPossession ? crocForward : rooForward;
            Transform defender = crocsInPossession ? rooDefender : crocDefender;
            // 2026-08-21 — chained contest only (chainDepth > 0, see
            // SECOND-CONTEST-BRIEF.md point 3). forward/defender are
            // wherever the FIRST contest (and the kick-out's own
            // defender-slide) left them — their original forward-line
            // spawn positions, which have nothing to do with where this
            // new contest is actually happening (anchored on rover's own
            // position, set by the caller to the kick-out's landing
            // spot). Without this they'd run at the new contest zone
            // from an arbitrary leftover position instead of a real
            // start line.
            if (chainDepth > 0)
            {
                forward.position = new Vector3(forward.position.x, forward.position.y, rover.position.z);
                defender.position = new Vector3(defender.position.x, defender.position.y, rover.position.z);
            }
            // Clamped for the same reason rover.position.z was clamped
            // above — this adds another kickDistance*0.5 (8 units)
            // beyond rover's already-clamped position, which alone can
            // still land outside the field on a deep chain hop.
            float peakZ = Mathf.Clamp(rover.position.z + runDir * kickDistance * 0.5f, -(goalZ - 2f), goalZ - 2f);
            // TEMPORARY diagnostic — Shaun: "it always seems to go back
            // to the defensive team's goals... this is where you are
            // confused." Print the actual numbers instead of reasoning
            // through the geometry a fourth time.
            float arriveByPeak = kickDropDuration + kickPause + kickDuration * 0.5f;
            // Real fix (2026-08-12, Shaun: "the speccy... forward now
            // starts behind runs up jumps really high on the opponents
            // shoulders and marks it at the peak"). Defender still just
            // runs into position (unchanged). The forward's own run is
            // now SpeccyLeap instead of a plain RunToZ — see its own
            // header comment for why this replaces the earlier
            // jumpFireAt/MarkJumpRoutine approach entirely rather than
            // layering on top of it.
            // Real fix (2026-08-12) — cut to the wide shot HERE, not
            // inside KickAway after the drop/pause. Forward's static
            // start moved from z=10 to z=5 for the speccy's run-up
            // ("starts behind"), which is also much closer to the
            // DEFAULT close camera — left on the close cam for the first
            // ~0.65s of the run (the old cut point), the forward loomed
            // huge/clipped at the bottom of frame. Cutting wide right as
            // the run begins avoids that window entirely.
            // 2026-08-21 — chained contest passes its own anchor instead
            // of the default goalZ-pinned pivot, which would point at
            // the wrong end of the ground for a contest happening
            // mid-field. Original (chainDepth==0) call is unaffected —
            // contestZ stays null.
            //
            // Real bug, found by re-deriving the actual numbers: this
            // passed rover.position.z (peakZ's own INPUT) instead of
            // peakZ itself — the contest happens at peakZ, which is
            // rover.position.z + runDir*kickDistance*0.5, a full 8 units
            // further out. The camera was aimed 8 units short of the
            // actual action on every chained contest. peakZ is already
            // computed above, use it directly.
            CutCameraForKick(runDir, chainDepth > 0 ? (float?)peakZ : null);
            _markHoldReleased = false;
            // 2026-08-19, Shaun: bring back the normal (non-leaping) mark
            // as the common case, speccy as the rarer highlight — real
            // footy, a speccy is the exception, not the default. This is
            // exactly the mechanic saved on tag afl-mark-nonspeccy-v1 back
            // when the speccy leap was first added (2026-08-12), now
            // reintroduced to coexist rather than replace it. kickHeight
            // has to track whichever jump actually plays (the project's
            // own "two numbers that have to agree" trap) — isSpeccy is
            // threaded into KickAway so the ball's arc always matches the
            // forward's real reach for whichever jump fires.
            bool isSpeccy = Random.value < speccyChance;
            if (isSpeccy) StartCoroutine(SpeccyLeap(forward, defender, peakZ, arriveByPeak));
            else StartCoroutine(RunToZ(forward, peakZ, arriveByPeak));
            // 2026-08-19, Shaun: "the kickout person's ability — looks
            // like they may not be able to move." Real bug, found by
            // checking the actual timing, not guessing: arriveByPeak
            // (~1.2s) starts counting from HERE, partway through
            // KickAway's own el timeline (jumpFireAt onward), so this
            // run doesn't actually finish until AFTER KickAway's loop
            // (kickDuration=1.1) has already exited into mark resolution
            // — meaning the later kick-out RunToZ(defender, ...) call
            // starts while this ORIGINAL one is still running, both
            // writing defender.position/.rotation at once. Exactly this
            // project's own documented recurring failure (one fact
            // written in two places). Stored so the kick-out step can
            // explicitly stop it first.
            _defenderRunToZ = StartCoroutine(RunToZ(defender, peakZ, arriveByPeak));
            yield return KickAway(rover, runDir, forward, defender, isSpeccy, crocsInPossession, chainDepth);
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
        // Real fix (2026-08-12, Shaun: "still kicks it well over the
        // forwards head"). 7 units read as more dramatic hang time, but
        // nobody measured it against what the SAME Hop() routine can
        // actually reach — Hop's own header comment has that number
        // measured directly: a full 1.65x hop brings the hand to world
        // y≈2.98, tuned to meet Day 1's own ball (groundY 1.0 + peakHeight
        // 2.1 = 3.1). The mark's ball freezes at footPos.y (≈0.3) +
        // kickHeight, so at 7 that's ≈7.3 — more than double the hand's
        // actual reach, invisible under the old close camera, obvious
        // once the wide kick-cut camera showed the catch itself. Brought
        // back down to meet the same proven reach (0.3 + 2.7 ≈ 3.0).
        //
        // Real fix (2026-08-12, Shaun: "the speccy... jump can be really
        // over the top"). SpeccyLeap's own reach is now much bigger than
        // the plain Hop's (speccyLeapHeightScale=4 vs 1.65) — same
        // formula as above, re-derived for the new height: hand ≈
        // 1.48 (arm-raised standing height) + speccyLeapHeightScale(4)
        // ≈ 5.48. kickHeight raised to match: 0.3 + 5.2 ≈ 5.5.
        public float kickHeight = 5.2f;
        // 2026-08-19: the normal mark's ball arc, matched to the plain
        // Hop's real reach (0.3 + 2.7 ≈ 3.0) — same math as kickHeight
        // above, just for the shorter, non-speccy jump.
        public float kickHeightNormal = 2.7f;
        // How often a mark plays out as the full running speccy leap
        // instead of a normal timed hop — kept deliberately rare so it
        // reads as a highlight, not the everyday case.
        [Range(0f, 1f)]
        public float speccyChance = 0.3f;
        // Real fix (2026-08-12, Shaun: "he runs up way to close"). The
        // forward's target (peakZ, below) is derived from this — at the
        // old value of 10 it only reached z≈12.8, barely past the centre
        // pack and nowhere near the goal at z=20. Widened so the forward
        // leads out to a real forward-line position just short of goal,
        // leaving room for Day 5's shot at goal rather than standing
        // half the ground away from it.
        public float kickDistance = 16f;
        public float kickDuration = 1.1f;
        public float kickDropDuration = 0.35f;

        // Real fix (2026-08-12). At 0.5 this pushed markDeadline
        // (peakT + this) out to 1.05, uncomfortably close to kickDuration
        // itself. 0.25 brings it to ~0.8 — still a genuine ~0.33s
        // acceptance window (well above the project's own "nothing
        // tighter than 0.25s" floor) once
        // markReactionCompensation is folded in.
        public float markPerfectWindow = 0.25f;
        public float markReactionCompensation = 0.17f;
        // 2026-08-19, Shaun: "the defender can jump and spoil the normal
        // mark" — a real bot contest for the non-speccy mark, same
        // randomized-reaction pattern already used for the initial ruck
        // tap (_botPressT). Jitter is wide relative to the spoil window
        // on purpose — the attacker still wins most of the time (they
        // know the timing, the defender is guessing), the spoil is the
        // occasional exception, not a coin flip. Both tunable in one
        // place if the balance needs adjusting after playtesting.
        public float defenderSpoilWindow = 0.20f;
        public float defenderSpoilJitter = 0.55f;
        // 2026-08-21, Shaun: "after some spoils another character gets
        // the ball goes for a run and kicks the ball into the forward
        // line exactly the same as what would happen in the centre" —
        // and "if they take the mark same thing as the start they kick
        // the ball towards the forward." Both outcomes (mark out of
        // range, and a spoil after the first kick-out) chain into
        // another TapBallAway instead of ending the round — real
        // football, but contest->spoil->clearer->contest has no natural
        // terminus, so it's capped.
        //
        // 2026-08-21, Shaun (live playtest — "all over the shop"): 3 let
        // the chain work the ball the FULL length of the ground in one
        // round (kick-out, mark, clearance, mark, clearance...), which
        // read as too sprawling/chaotic to actually follow. Capped to 1:
        // the kick-out's own first contest still gets its full mark-or-
        // spoiled-clearance treatment, but if THAT needs to chain again
        // (a spoil on it, or an out-of-range mark), it stops there and
        // resets instead of working further up the ground.
        public int maxChainDepth = 1;

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
        System.Collections.IEnumerator KickAway(Transform t, float zDir, Transform forward, Transform defender, bool isSpeccy, bool humanControlled, int chainDepth = 0)
        {
            if (!t || !ball) yield break;
            int roundAtStart = _roundId;
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
            // Same field-bounds clamp as TapBallAway's peakZ, applied
            // here too — this kick's own full kickDistance (16, not
            // halved) can independently push the ball's landing spot
            // outside the field on a deep chain hop, one level below
            // where the TapBallAway-level clamps already catch it.
            float kickEndZ = Mathf.Clamp(kickStart.z + zDir * kickDistance, -(goalZ - 2f), goalZ - 2f);
            Vector3 kickEnd = new Vector3(kickStart.x, kickStart.y, kickEndZ);
            float peakT = kickDuration * 0.5f;
            float markTargetT = peakT + markReactionCompensation;
            float markDeadline = Mathf.Min(peakT + markPerfectWindow, kickDuration);
            bool markPressed = false;
            float markBestErr = float.MaxValue;
            bool markResolved = false;
            bool defenderSpoiled = false;
            bool defendPressed = false;
            // Real fix (2026-08-12, Shaun: "the kick is now way of the
            // forward and defender for the mark scene"). Not a physics
            // change — the wide kick-cut camera just made a mismatch
            // visible that the old close camera never showed: the ball
            // used to keep flying (and only visually freeze) at
            // markDeadline, ~95% of the way to kickEnd, while the forward
            // stands at kickDistance*0.5 (the arc's own peak spot, where
            // RunToZ sends them — see TapBallAway). Same principle Day 1's
            // own ruck contest already uses for its ball: freeze at the
            // ball's true visual peak, not later, so it stops exactly
            // where the forward is standing instead of sailing past them.
            bool ballFrozen = false;
            // Real fix (2026-08-12, Shaun: "the speccy... jumps really
            // high on the opponents shoulders"). The forward's leap is
            // now SpeccyLeap, started back in TapBallAway alongside the
            // run itself (not fired from in here) — a real leap with a
            // run-up needs more lead time than fits inside the kick's own
            // flight window. This loop still owns when the outcome
            // (marked/spilled) is decided and signals SpeccyLeap via
            // _markHoldReleased/_markHoldSucceeded, same mechanism as
            // before.
            //
            // 2026-08-19: on a normal (non-speccy) mark, both forward and
            // defender now use NormalMarkHop instead of the old plain
            // Hop() — Hop() is a fixed-duration fire-and-forget animation
            // that lands and returns to ground on its own clock regardless
            // of when markDeadline actually resolves (originally tuned so
            // tight against markPerfectWindow that a resolve could land
            // AFTER the hop had already returned to the ground — "says
            // mark but the ball is on the ground", the exact bug this
            // file's own history has already hit once on tag
            // afl-mark-nonspeccy-v1). NormalMarkHop rises then HOLDS at
            // peak until told to release, the same pattern SpeccyLeap
            // already uses above — no clock to race against.
            bool jumpFired = false;
            float jumpFireAt = peakT - normalMarkHopRiseDuration;
            // Defender's spoil attempt — Shaun: "the defender can jump and
            // spoil the normal mark." Same randomized-reaction idiom as
            // the initial ruck tap's _botPressT: a wide jitter relative to
            // the spoil window keeps the attacker winning most of the
            // time, the spoil is a real but occasional contest, not a
            // coin flip.
            float defenderSpoilT = markTargetT + Random.Range(-defenderSpoilJitter, defenderSpoilJitter);
            // 2026-08-19, Shaun: "the human kicks at goal for themself and
            // ai" applied to the mark too, once it became clear the same
            // gap explained "watched without tapping and it still played
            // out" — Day1Input.TapDown used to be read unconditionally
            // here regardless of which team was forward, so the human's
            // own tap (or lack of one) never actually mattered when Roo
            // was attacking. Same pattern as TakeShotAtGoal's aiTapAt: a
            // real simulated attempt, not an auto-win, when it's not the
            // human's turn to act.
            float aiMarkTapAt = humanControlled ? 0f : markTargetT + Random.Range(-0.15f, 0.15f);
            // 2026-08-19, Shaun: "there is only one button" — with one
            // tap doing everything, there was no way to know whether this
            // beat wanted you marking or defending. No message was ever
            // set during this whole window (the last text on screen was
            // whatever the run-in left behind), so a tap here was
            // genuinely a guess. Telling the player their actual role for
            // this specific round is the real fix, not a code bug in the
            // tap detection itself (checked — it's sound).
            _message = humanControlled ? "Go up for the mark!" : "Defend! Tap to spoil!";
            el = 0f;
            while (el < kickDuration)
            {
                el += Time.deltaTime;
                float f = Mathf.Clamp01(el / kickDuration);
                if (!ballFrozen)
                {
                    float arc = Mathf.Sin(f * Mathf.PI) * (isSpeccy ? kickHeight : kickHeightNormal);
                    ball.position = Vector3.Lerp(kickStart, kickEnd, f) + Vector3.up * arc;
                    if (el >= peakT) ballFrozen = true;
                }

                if (!isSpeccy && !jumpFired && el >= jumpFireAt)
                {
                    jumpFired = true;
                    StartCoroutine(NormalMarkHop(forward));
                    StartCoroutine(NormalMarkHop(defender));
                }

                // Same best-tap-counts pattern as day 1 (Shaun: "i just
                // keep hitting tap kid would do that") — mashing helps,
                // not just the first or last press.
                if (humanControlled)
                {
                    if (Day1Input.TapDown)
                    {
                        markPressed = true;
                        float err = Mathf.Abs(el - markTargetT);
                        if (err < markBestErr) markBestErr = err;
                    }
                }
                else if (!markPressed && el >= aiMarkTapAt)
                {
                    markPressed = true;
                    markBestErr = Mathf.Abs(aiMarkTapAt - markTargetT);
                }

                // 2026-08-19, Shaun: the same "movie" gap the mark and shot
                // already had — when Roo is forward, Croc is defending,
                // and defending NEVER read real input at all, always a
                // bot roll regardless of team. Day1Input.TapDown is free
                // in this branch (the AI-mark check above doesn't read
                // it), so this is a genuine human spoil attempt, not a
                // second thing fighting over the same tap.
                if (!humanControlled && Day1Input.TapDown) defendPressed = true;

                if (!markResolved && el >= markDeadline)
                {
                    markResolved = true;
                    // 2026-08-19, Shaun: a real leap for the mark should
                    // never spill — any genuine tap during the window
                    // secures it now, no more precision-timing drop. The
                    // defender's own timed jump is the only thing that can
                    // still deny it on a normal (non-speccy) mark. Human
                    // defends with a real tap (any tap counts, same rule
                    // as marking); AI defends via the randomized roll.
                    defenderSpoiled = !isSpeccy && (humanControlled
                        ? Mathf.Abs(defenderSpoilT - markTargetT) <= defenderSpoilWindow
                        : defendPressed);
                    bool marked = markPressed && !defenderSpoiled;
                    _message = marked ? "MARK!" : (defenderSpoiled ? "Spoiled by the defender!" : "Spilled!");
                    // 2026-08-21 — real bug: this called the unmirrored
                    // CutCameraToMarkCloseup(forward) (fixed +7 X
                    // offset). Rarely showed at the centre bounce since
                    // the forward starts near the middle, but chained
                    // contests put players out at ±1.6/±2.6 — on the -X
                    // side this puts the camera INSIDE the post cluster's
                    // sightline, the exact occlusion the mirrored
                    // overload (already used for the kick-out) exists to
                    // avoid.
                    if (marked) CutCameraToMarkCloseup(forward, Mathf.Sign(forward.position.x == 0f ? 1f : forward.position.x));
                    _markHoldSucceeded = marked;
                    // 2026-08-28: on the spoil path the forward must stay up until the
                    // punch connects. Releasing here meant the forward was already
                    // descending before the defender left the ground at all - two
                    // consecutive jumps, never a contest. Every spoil branch below
                    // releases it; both hold loops also escape on a round change.
                    if (!defenderSpoiled) _markHoldReleased = true;
                }
                yield return null;
            }

            // Real fix (2026-08-12, Shaun: "now let move on to the next
            // phase after the mark they go back and have a shot at
            // goal"). This used to fire MarkCatchRoutine as a detached
            // StartCoroutine and return immediately — fine when a spill
            // just meant "round's basically over," wrong now that a real
            // mark means several more seconds of real gameplay (walk
            // back, run in, kick at goal) still has to happen before the
            // round is actually done. KickAway now YIELDS through the
            // whole chain, so TapBallAway's own _sequenceComplete/reset
            // timer (which fires shortly after this returns) doesn't cut
            // the shot off mid-way — same class of bug as the earlier
            // ball-position race, just at the sequencing level instead
            // of the position-writing level.
            bool markedResult = markPressed && markResolved && !defenderSpoiled;
            yield return MarkCatchRoutine(forward, markedResult);
            if (markedResult)
            {
                // 2026-08-21, Shaun: previously scoped the chained contest
                // (chainDepth > 0, after a kick-out) to stop dead right at
                // a confirmed mark — deliberate, so the mark itself
                // (positioning, camera) could be verified in isolation
                // first (see git history for that intermediate state).
                // Confirmed working; Shaun then asked for the natural next
                // step: "getting up to the forward down the other end
                // they just need to go back and kick the goal down the
                // other end" — a mark at chainDepth > 0 now uses the exact
                // same shot-at-goal / continue-chain logic as the original
                // centre-bounce contest (chainDepth == 0), below. No
                // special-casing needed — TakeShotAtGoal and
                // ContinueChainOrEnd already take kicker/zDir/chainDepth
                // as arguments, not an assumption about which contest this
                // is.
                // 2026-08-21, Shaun: "if they take the mark same thing as
                // the start they kick the ball towards the forward" — a
                // mark taken mid-ground shouldn't jump straight to a set
                // shot from 60 metres out, only one taken within real
                // shooting range does. shotRangeZ is DERIVED from
                // kickDistance (how far a kick actually travels) rather
                // than picked by eye — if kick power is ever re-tuned,
                // this follows automatically instead of silently drifting
                // out of sync with it (the exact trap CLAUDE.md warns
                // about at its own top).
                float shotRangeZ = goalZ - kickDistance;
                if (Mathf.Abs(ball.position.z) >= shotRangeZ)
                {
                    yield return TakeShotAtGoal(forward, zDir, humanControlled);
                }
                else
                {
                    // Out of range — the marker plays on, same team,
                    // same direction (no reverse), continuing toward
                    // their own forward line via the same TapBallAway
                    // chain as everything else.
                    //
                    // 2026-08-21, Shaun: "the ball going in the air until
                    // ground level like the kickout just pause then bring
                    // it down" — same class of bug as the spoil branch
                    // below and the kick-out's own original defect: the
                    // ball is still wherever MarkCatchRoutine left it
                    // (elevated, mid-catch height), and nothing resets
                    // the camera before chaining into the next contest.
                    ball.position = new Vector3(ball.position.x, groundY, ball.position.z);
                    CutCameraToDefault();
                    yield return new WaitForSeconds(catchPause);
                    if (_roundId != roundAtStart) yield break;
                    yield return ContinueChainOrEnd(humanControlled, forward, chainDepth);
                }
            }
            else if (defenderSpoiled)
            {
                // 2026-08-21, Shaun (correcting SECOND-CONTEST-BRIEF.md,
                // via CLEARANCE-CHAIN-BRIEF.md): a rushed behind + literal
                // kick-out only makes real football sense at the very
                // first contest — the only one that happens right at a
                // goal line, where a spoil can actually produce a score.
                // Every later spoil (chainDepth > 0) happens mid-ground,
                // same as any other loose ball — it gets the same simple
                // clearance handoff as an uncontested drop, not another
                // goal-line sequence.
                if (chainDepth == 0)
                {
                // 2026-08-19, Shaun: "the defender spoiled the ball
                // through the points, no second play, just a straight
                // spoil through the points if they spoil it." A real,
                // authentic AFL moment — spoiling it hard under pressure
                // can knock it straight through the defender's own behind
                // posts for a rushed point, rather than a clean clearance.
                _message = "Rushed behind — one point!";
                // 2026-08-21, Shaun (live playtest): "the ball randomly
                // goes to the top of the goal posts" — the REAL root
                // cause, found by tracing actual numbers, not the camera
                // at all (that was a real but secondary issue, already
                // fixed above). The ball was still frozen at the mark
                // contest's peak jump height (groundY + kickHeightNormal
                // = 1.0 + 2.7 = 3.7) from before the spoil — ALREADY
                // taller than the goal posts themselves (3.2) — and
                // nothing ever brought it back down to a sensible resting
                // height before this whole rushed-behind + kick-out
                // sequence, so it visibly hovered near/above the post the
                // entire time. Reset to ground level here, once, before
                // either kick arc starts.
                // 2026-08-28: the defender punches it through at the top of the leap.
                // The ball used to be dropped to ground here and then simply fly, so a
                // rushed behind was indistinguishable from a stray ball. He is placed
                // under it so the hand genuinely reaches it, rather than the ball being
                // teleported into his fist.
                // Captured BEFORE the defender is moved - the comment below is explicit
                // that this must read his spawn x, not a value set from the ball.
                float side = Mathf.Sign(defender.position.x == 0f ? 1f : defender.position.x);
                defender.position = new Vector3(ball.position.x, defender.position.y,
                    ball.position.z - zDir * 0.35f);
                defender.rotation = Quaternion.Euler(0f, zDir > 0f ? 0f : 180f, 0f);
                SpoilPunch(defender);
                yield return new WaitForSeconds(hopDuration * 0.5f);
                if (_roundId != roundAtStart) yield break;
                // Contest resolved at the peak - only now does the forward come down.
                _markHoldReleased = true;
                // 2026-08-21 — real fix, found by tracing coordinates
                // rather than guessing at symptoms again (see
                // KICKOUT-BRIEF.md). Ball and kicker must end up on the
                // SAME side. behindTarget used to hardcode +1.6 while
                // clearX (below) followed the defender's spawn sign — so
                // half the time they finished 4.2 units apart on
                // OPPOSITE sides of the goal, which is why "can't see the
                // player near the ball" survived three earlier fix
                // attempts that each addressed a real but secondary
                // issue. Captured once here, before the slide, while
                // defender.position.x still holds the spawn value.
                side = Mathf.Sign(defender.position.x == 0f ? 1f : defender.position.x);
                Vector3 behindKickStart = ball.position;
                // target the ground, not the punch height - a punched ball falls
                Vector3 behindTarget = new Vector3(side * 1.6f, groundY, zDir * goalZ);
                float behindEl = 0f;
                while (behindEl < spoilPunchDuration)
                {
                    if (_roundId != roundAtStart) yield break;
                    behindEl += Time.deltaTime;
                    float f = Mathf.Clamp01(behindEl / spoilPunchDuration);
                    float arc = Mathf.Sin(f * Mathf.PI) * spoilPunchHeight;
                    ball.position = Vector3.Lerp(behindKickStart, behindTarget, f) + Vector3.up * arc;
                    yield return null;
                }
                ball.position = behindTarget;

                // 2026-08-19, Shaun: "next step, kick out from full back
                // after a point." Real AFL — after a behind, the
                // defending team's own player kicks it back into play
                // from the goal square. Reuses the defender who was
                // already right there for the spoil. Deliberately
                // minimal again: run to the goal square, kick it back
                // toward centre, stop — no new contest chained on yet.
                // Stop the original mark-contest run before starting a
                // new one for the same character — see the field's own
                // comment for the confirmed race this was causing.
                if (_defenderRunToZ != null) StopCoroutine(_defenderRunToZ);
                Vector3 goalSquare = new Vector3(0f, defender.position.y, zDir * goalZ);
                yield return RunToZ(defender, goalSquare.z, 0.6f);
                if (_roundId != roundAtStart) yield break;
                // 2026-08-21, Shaun (live playtest): still couldn't
                // actually see the player even after the ball-height and
                // camera fixes above — RunToZ only ever moves Z, so the
                // defender's X stayed wherever the mark contest left them
                // (~±0.9, the defender-zone spawn X from MainBuildScript)
                // — almost exactly between the two inner goal posts
                // (±0.6). The close-up camera's fixed (7,3,0) world-space
                // offset then looked straight through a post to reach
                // them. Slide clear of the post cluster (posts span
                // -1.3..1.3) before the close-up cuts in — same `side` as
                // the ball above now, not a separately-derived sign.
                float clearX = side * 2.6f;
                float slideEl = 0f;
                Vector3 slideStart = defender.position;
                while (slideEl < 0.3f)
                {
                    slideEl += Time.deltaTime;
                    defender.position = Vector3.Lerp(slideStart, new Vector3(clearX, slideStart.y, slideStart.z), Mathf.Clamp01(slideEl / 0.3f));
                    yield return null;
                }
                if (_roundId != roundAtStart) yield break;

                // 2026-08-19, Shaun: "pause, zoom in on the player, then
                // they kick it clearly like they do kicking out of the
                // centre." Reusing the same two camera beats already
                // proven elsewhere: CutCameraToMarkCloseup for the zoom
                // (same one a mark uses), CutCameraForKick for the wide
                // framing during the kick itself (same one the centre's
                // own rover kick uses) — direction is -zDir here since
                // the fullback kicks back toward centre, not further
                // into the attacking end.
                _message = "Kicks out from fullback!";
                // 2026-08-19, Shaun: "the issue is turning around and
                // kicking the other way." Real bug — RunToZ sets facing
                // from net movement direction, but the defender is
                // already standing right at/near the goal square from
                // the mark contest, so that "run" can be a near-zero
                // distance with an arbitrary resulting facing. Setting
                // the kick-out facing explicitly instead of trusting
                // RunToZ's rotation for what's practically a non-move.
                defender.rotation = Quaternion.Euler(0, zDir > 0 ? 180 : 0, 0);
                // 2026-08-21 — mirrored variant, not the shared
                // CutCameraToMarkCloseup(Transform) the mark beat uses
                // (that framing is already signed off, left untouched).
                // The original's fixed +X offset means when the subject
                // ends up on -X (half the spawn sides), the camera sits
                // INSIDE the post cluster's sightline — the exact
                // occlusion the slide above exists to avoid.
                CutCameraToMarkCloseup(defender, side);
                yield return new WaitForSeconds(kickOutPause);
                if (_roundId != roundAtStart) yield break;
                // 2026-08-21, Shaun: "you need to zoom right in make sure
                // you have the camera on the player then make sure you
                // see them kick the ball out." Stay on the zoomed-in
                // close-up through the WHOLE kick motion (yielded, not
                // fired-and-forgotten) — the wide kick-arc camera only
                // cuts in once the leg has actually snapped forward, so
                // the kick itself is genuinely visible before the ball
                // starts flying, not hidden behind a camera cut.
                //
                // 2026-08-21 — the actual defect this whole beat had:
                // the ball was landing at behindTarget and never moving
                // again until the kick-out arc itself, while the leg
                // snapped at the defender's own position — a metre-plus
                // away even on the correctly-signed side. Kicker and
                // ball were two unrelated objects that happened to
                // animate at the same time. Put it on the boot and keep
                // it there through the motion, same idiom
                // MarkCatchRoutine already uses for the hand (track the
                // real bone, don't guess a nearby point).
                var boot = FindDeepChild(defender, "RightFoot");
                if (ball) ball.position = boot ? boot.position : defender.position + Vector3.up * (groundY * 0.5f);
                yield return StartCoroutine(KickMotionWithBall(defender, boot, kickMotionDuration));
                if (_roundId != roundAtStart) yield break;
                // 2026-08-21, Shaun (live playtest): "camera nowhere near
                // the person" — first attempted fix (flipping this to
                // plain zDir) was still wrong. Real problem: this whole
                // kick travels the full ground length (goal square to
                // centre, ~20 units), far more than the static, non-
                // panning CutCameraForKick was ever built to cover (see
                // CutCameraForKickOut's own comment) — so no single sign
                // choice on the OLD camera call would have fixed it, a
                // dedicated wide shot was the actual fix needed.
                // 2026-08-21, Shaun: "does not have to go all the way to
                // the centre" — a real fullback kick-out doesn't need to
                // reach dead centre, and a shorter traverse is also
                // easier to frame cleanly in one static shot.
                float kickOutTargetZ = zDir * goalZ - zDir * kickOutDistance;
                CutCameraForKickOut(zDir * goalZ, kickOutTargetZ);

                Vector3 kickOutStart = ball.position;
                Vector3 kickOutTarget = new Vector3(0f, kickOutStart.y, kickOutTargetZ);
                float kickOutEl = 0f;
                while (kickOutEl < shotKickDuration)
                {
                    if (_roundId != roundAtStart) yield break;
                    kickOutEl += Time.deltaTime;
                    float f = Mathf.Clamp01(kickOutEl / shotKickDuration);
                    float arc = Mathf.Sin(f * Mathf.PI) * shotKickHeight;
                    ball.position = Vector3.Lerp(kickOutStart, kickOutTarget, f) + Vector3.up * arc;
                    yield return null;
                }
                ball.position = kickOutTarget;

                // 2026-08-21 — second contest chain (see
                // SECOND-CONTEST-BRIEF.md, and note this whole block only
                // runs at chainDepth==0 per the outer check above, so the
                // chain is always allowed here — the earlier depth guard
                // that lived on this specific call was removed, the real
                // bound now lives in ContinueChainOrEnd/maxChainDepth for
                // every OTHER spoil in the chain). Reposition defender
                // (the kick-out kicker, now acting as this new contest's
                // "rover") onto the ball's actual landing spot —
                // TapBallAway's own peakZ and camera pivot both derive
                // from rover.position.z, so this is what anchors the
                // whole chained contest on where the ball really is
                // instead of where the kicker used to stand
                // (SECOND-CONTEST-BRIEF.md point 1).
                defender.position = kickOutTarget;
                // Possession flips to the team that just kicked out —
                // TapBallAway derives their run direction from nothing
                // but which team that is (see its own header comment on
                // the reverseDirection bug this replaced); no separate
                // direction override needed or correct here.
                yield return TapBallAway(crocsInPossession: !humanControlled, kickerOverride: defender, chainDepth: chainDepth + 1);
                }
                else
                {
                    // 2026-08-21 — a spoil past the first contest is just
                    // a mid-ground loose ball, same treatment as an
                    // uncontested drop below (no goal-line/behind
                    // mechanic — that only makes sense right at a goal).
                    // Mid-ground spoil: no punch beat here, so release the forward
                    // immediately - otherwise it hangs at peak for the rest of the round.
                    _markHoldReleased = true;
                    _message = "Spoiled — cleared away!";
                    // 2026-08-21, Shaun: "the ball going in the air until
                    // ground level like the kickout just pause then bring
                    // it down" — same real bug as the kick-out's original
                    // "floats near the goal post" defect: the ball is
                    // still frozen at this contest's own peak jump height
                    // from before the spoil, never reset to a sensible
                    // resting height. Also reset the camera to the known-
                    // safe default here — this branch didn't cut to
                    // anything of its own, so it was inheriting whatever
                    // pivot the PREVIOUS chain hop's wide kick-shot left
                    // behind, which is exactly the kind of drift that
                    // produced a camera pointed at the sky in testing.
                    ball.position = new Vector3(ball.position.x, groundY, ball.position.z);
                    CutCameraToDefault();
                    yield return new WaitForSeconds(catchPause);
                    if (_roundId != roundAtStart) yield break;
                    Transform spoilClearer = humanControlled ? rooClearer : crocClearer;
                    spoilClearer.position = new Vector3(spoilClearer.position.x, spoilClearer.position.y, ball.position.z);
                    yield return RunToZ(spoilClearer, ball.position.z, 0.4f);
                    if (_roundId != roundAtStart) yield break;
                    yield return MarkCatchRoutine(spoilClearer, true);
                    if (_roundId != roundAtStart) yield break;
                    yield return ContinueChainOrEnd(!humanControlled, spoilClearer, chainDepth);
                }
            }
            else
            {
                // 2026-08-19, Shaun: "so the spoil, after the spoil the
                // ball gets into the other player's hands, that's the next
                // step, just add that, nothing else after that, we add the
                // next step. This has only worked doing one step at a
                // time." A genuine uncontested drop (nobody tapped, not an
                // active spoil) still gets the clearance treatment — the
                // defending team's clearer takes it.
                _message = "Cleared away!";
                Transform clearer = humanControlled ? rooClearer : crocClearer;
                yield return RunToZ(clearer, forward.position.z, 0.6f);
                if (_roundId != roundAtStart) yield break;
                yield return MarkCatchRoutine(clearer, true);
                if (_roundId != roundAtStart) yield break;
                // 2026-08-21, Shaun: "the ball going in the air until
                // ground level like the kickout just pause then bring it
                // down" — same class of bug as the other two chain
                // continuations: MarkCatchRoutine leaves the ball at the
                // clearer's hand (elevated), and nothing resets the
                // camera before the next contest. Ground it and cut to
                // the known-safe default before chaining on.
                ball.position = new Vector3(ball.position.x, groundY, ball.position.z);
                CutCameraToDefault();
                yield return new WaitForSeconds(catchPause);
                if (_roundId != roundAtStart) yield break;
                // 2026-08-21, Shaun: "after some spoils another character
                // gets the ball goes for a run and kicks the ball into
                // the forward line exactly the same as what would happen
                // in the centre" — the clearer doesn't just receive it
                // and stop, they continue the chain the same way
                // everything else in this game does.
                // 2026-08-28, Shaun: "easier way out of defence could be for when
                // it says clear it away the defender runs with the ball and kicks
                // it forwards to aoid it going back to middle". ContinueChainOrEnd
                // hands off through TapBallAway, which brings the ball to the
                // team's rover in the middle - the ball went backwards. The
                // clearer now runs it out himself and kicks forward, and it lands
                // in a contest because that is what KickAway stages.
                if (chainDepth < maxChainDepth)
                {
                    bool clearCrocs = !humanControlled;
                    float clearDir = clearCrocs ? 1f : -1f;
                    // 2026-08-28, Shaun: "the cleared away stuff the players are facing
                    // the wrong way they need to start running in the corect direction".
                    // RunToZ above turned him back toward the ball, so he began the
                    // clearance still facing his own goal. Face the way he is about to
                    // run before he starts.
                    clearer.rotation = Quaternion.Euler(0f, clearDir > 0f ? 0f : 180f, 0f);
                    yield return RunStraight(clearer, clearDir);
                    if (_roundId != roundAtStart) yield break;
                    yield return KickAway(clearer, clearDir,
                        clearCrocs ? crocForward : rooForward,
                        clearCrocs ? rooDefender : crocDefender,
                        false, clearCrocs, chainDepth + 1);
                }
                else
                {
                    _message = "Time's up — turnover!";
                }
            }
            CutCameraToDefault();
        }

        // 2026-08-21 — shared by "Cleared away!" (an uncontested drop)
        // and a spoil on any contest past the first (see below — only
        // the very first, goal-line contest gets the rushed-behind/
        // kick-out treatment; every later spoil is just a clearance,
        // same as this). Continues via the same TapBallAway everything
        // else in this game reuses, bounded by maxChainDepth so
        // contest->spoil->clearer->contest can't recurse forever. At the
        // cap, end honestly (CLAUDE.md's placeholder-ending rule)
        // instead of stalling into not-yet-built work.
        System.Collections.IEnumerator ContinueChainOrEnd(bool crocsInPossession, Transform newRover, int chainDepth)
        {
            if (chainDepth >= maxChainDepth)
            {
                _message = "Time's up — turnover!";
                yield break;
            }
            yield return TapBallAway(crocsInPossession: crocsInPossession, kickerOverride: newRover, chainDepth: chainDepth + 1);
        }

        // 2026-08-21 — contestZ lets a chained contest (e.g. the second
        // mark after a kick-out) override the pivot instead of always
        // pinning to goalZ. pivotZ = zDir * (goalZ - 5f) is only correct
        // when the contest is heading INTO the goal (the centre bounce's
        // own rover kick) — a contest happening mid-ground, moving away
        // from the goal, needs the pivot to follow it instead. Existing
        // callers pass one argument and are unaffected.
        void CutCameraForKick(float zDir, float? contestZ = null)
        {
            if (!_mainCam) return;
            // Pivot sits between the forward's zone (~z=10) and the goal
            // (goalZ) so both the ball's landing and the posts are framed
            // together, not just one or the other.
            float pivotZ = contestZ ?? (zDir * (goalZ - 5f));
            _mainCam.transform.position = new Vector3(kickCamSide, kickCamHeight, pivotZ);
            // Real fix (2026-08-12, same pass as the speccy). LookAt
            // height raised from 1.5 to 3 — with the leap now reaching
            // speccyLeapHeightScale (4) above standing height, framing on
            // the OLD ground-level lookAt pushed the peak of the jump up
            // near/above the top of frame. Centering higher keeps the
            // whole leap comfortably in view.
            _mainCam.transform.LookAt(new Vector3(0, 3f, pivotZ));
        }

        // 2026-08-21, Shaun (live playtest, second pass — the sign fix
        // alone didn't actually fix it): CutCameraForKick is a static,
        // non-tracking side-on shot tuned for the FORWARD's own kick
        // (~kickDistance=16 units, pivoted 5 units short of the goal).
        // The kick-out travels the ENTIRE ground length instead — goal
        // square (z=goalZ) all the way to centre (z=0), a full ~20-unit
        // traverse — so reusing that same fixed, un-panning camera meant
        // most of the flight happened off-frame regardless of which end
        // it pivoted toward; only the very start (near the post) was
        // ever actually in shot. Dedicated wide shot instead: pivots at
        // the true midpoint of this specific kick's path, pulled back
        // further (1.6x the normal side/height) so the whole traverse
        // fits in one static frame instead of just a slice of it.
        void CutCameraForKickOut(float startZ, float endZ)
        {
            if (!_mainCam) return;
            float pivotZ = (startZ + endZ) * 0.5f;
            _mainCam.transform.position = new Vector3(kickCamSide * 1.6f, kickCamHeight * 1.6f, pivotZ);
            _mainCam.transform.LookAt(new Vector3(0, 3f, pivotZ));
        }

        void CutCameraToDefault()
        {
            if (!_mainCam) return;
            _mainCam.transform.position = _camDefaultPos;
            _mainCam.transform.rotation = _camDefaultRot;
        }

        // Real fix (2026-08-12, Shaun: "really being able to see the
        // person grabbing the mark"). The wide kick-cut framing (above)
        // is built to show the goal and the ball's whole flight, which
        // makes the forward small in frame — good for the flight, not
        // for the catch itself. Zoomed tight on the forward's actual
        // position instead, side-on (same reasoning as the wide cut:
        // avoids needing to know which way they're facing).
        // Offsets are relative to forward.position, which by the time
        // this fires (markDeadline, after SpeccyLeap's rise) already
        // sits near the TOP of its own leap — so these don't need to
        // grow with speccyLeapHeightScale, they already track it.
        // Widened slightly (was 6/3.5) to give the much bigger leap
        // (over-the-top per Shaun's ask) room to read in frame.
        void CutCameraToMarkCloseup(Transform forward)
        {
            if (!_mainCam || !forward) return;
            _mainCam.transform.position = forward.position + new Vector3(7f, 3f, 0f);
            _mainCam.transform.LookAt(forward.position + Vector3.up * 1.2f);
        }

        // 2026-08-21 — mirrored variant for the kick-out beat only. Do
        // not fold `side` into the original above: the mark beat uses
        // that one as-is and Shaun has already signed off on its framing
        // ("really being able to see the person grabbing the mark") —
        // changing shared behaviour here would silently alter a beat
        // that's currently accepted. The fixed +X offset above means
        // that when the subject is actually on -X (half of the kick-out's
        // two spawn sides), the camera ends up INSIDE the post cluster's
        // sightline — the exact occlusion the kick-out's defender-slide
        // exists to avoid. Kick-out uses this; the mark keeps the
        // original.
        void CutCameraToMarkCloseup(Transform subject, float side)
        {
            if (!_mainCam || !subject) return;
            _mainCam.transform.position = subject.position + new Vector3(side * 7f, 3f, 0f);
            _mainCam.transform.LookAt(subject.position + Vector3.up * 1.2f);
        }

        // Real fix (2026-08-12, Shaun: "with a mark the forward catches
        // the ball") — a marked ball ends up genuinely held in the
        // forward's hand, tracked live the same way the run/catch
        // elsewhere in this file already does, not just a text message.
        //
        // Real fix (2026-08-12, Shaun: "just want the forward clearly
        // jumping into the ball and marking at highest point") — the jump
        // itself no longer starts here, it's SpeccyLeap (fired from
        // TapBallAway). This only decides the ball's fate.
        //
        // Real fix (2026-08-12, Shaun: "after the mark they go back and
        // have a shot at goal") — no longer decides camera-default or
        // message (KickAway does both now, since it needs to know the
        // outcome before deciding whether a shot follows) and no longer
        // fires itself detached — KickAway yields on this directly so the
        // whole chain (catch hold, then a shot if marked) finishes before
        // the round is allowed to reset.
        public float markHoldBeforeShot = 0.6f;

        System.Collections.IEnumerator MarkCatchRoutine(Transform forward, bool marked)
        {
            if (!marked) yield break;
            int roundAtStart = _roundId;
            // Ball snaps straight to the forward's actual raised hand —
            // no extra wait needed now, the jump already fired early
            // enough to be at/near its own peak by this point. Then keeps
            // tracking it live, same principle as why the run's ball-on-
            // a-fixed-offset looked wrong earlier tonight (2026-08-12,
            // "balancing the ball on there tummy"): track the real bone,
            // not a guess.
            var hand = FindDeepChild(forward, "RightHand");
            float holdEl = 0f;
            while (holdEl < markHoldBeforeShot)
            {
                // Bail the moment a new round has started (see the
                // _roundId comment above) — otherwise this can keep
                // overwriting the ball's position after BeginThrow already
                // reset it for the next contest.
                if (_roundId != roundAtStart) yield break;
                holdEl += Time.deltaTime;
                if (hand && ball) ball.position = hand.position;
                yield return null;
            }
        }

        // Real fix (2026-08-12, Shaun: "just needs to be slowed down a
        // bit the shot at goal its a bit rushed"). Step-back/pause/run-in
        // durations all lengthened a bit — the whole "go back and take a
        // shot" beat needs to read as deliberate, not hurried.
        public float shotStepBackDistance = 6f;
        public float shotStepBackDuration = 1f;
        public float shotSetupPause = 0.4f;
        public float shotRunInDuration = 0.9f;
        public float shotDropDuration = 0.3f;
        public float shotKickHeight = 3f;
        public float shotKickDuration = 0.9f;
        // How far off-centre (world X) a mistimed kick drifts — enough to
        // clearly pass outside the posts (which span -1.3 to 1.3, see
        // Day1BuildScript's BuildGoalPosts) rather than an ambiguous
        // near-miss.
        public float shotMissSpread = 2.4f;
        public float shotResultHold = 1f;

        // Real fix (2026-08-12). Tap-to-aim-on-the-3D-field (previous
        // pass) got direct, blunt feedback once actually played: "no
        // idea when to push buttons what to do... chaotic." Reverted —
        // per Shaun's own follow-up ("red light thing" / "even a power
        // button visible"), the fix is the OPPOSITE direction: back to
        // the single existing TAP button (same one used for every other
        // action in this game, no new input type at all), with an
        // explicit, unmissable RED/GREEN visual cue instead of an
        // implicit one. A power bar rises once, red for most of its
        // range, GREEN only in the scoring window near the top, red
        // again if it runs all the way out — tap once, whenever you
        // choose, ideally while it's green.
        public float shotPowerRiseDuration = 2.5f;
        // 2026-08-28, Shaun: "probaly have a really quick power button to get the
        // snap goal". A snap is taken at pace - the bar sweeps in well under half
        // the time a set shot gets, so it has to be hit sharply.
        public float snapPowerRiseDuration = 1.0f;
        public float shotPowerGreenMin = 0.62f;
        public float shotPowerGreenMax = 0.85f;
        bool _shotBarVisible;
        float _shotBarValue;

        // Real fix (2026-08-12, Shaun: "now let move on to the next
        // phase after the mark they go back and have a shot at goal").
        // Day 5 of the canonical plan. "They go back": kicker steps
        // straight back (away from goal) for a run-up, then straight
        // back in to their marking spot, matching the straight-line-only
        // movement rule everywhere else.
        //
        // "Decide the outcome, then perform it" (this file's own
        // recurring principle — see the Day 1 ball and the mark's ball)
        // still holds, just simpler here than the mark's freeze-then-
        // tween version: the aim tap resolves the outcome (and the exact
        // target) before the ball is even kicked, so the arc can just
        // fly straight at its real final target from the start — no
        // need to fake a mid-flight change of mind.
        // Real fix (2026-08-12, Shaun: "after the mark is taken a pause
        // will help" / "will be a pause between shots"). On top of
        // markHoldBeforeShot (the ball-in-hand hold right after the
        // catch), a distinct beat here before the step-back even starts
        // — so "MARK!" has room to land before the scene moves on to the
        // shot, and shotResultHold (below) does the same job between one
        // shot's result and the next round's centre bounce.
        public float shotStartPause = 0.5f;

        System.Collections.IEnumerator TakeShotAtGoal(Transform kicker, float zDir, bool humanControlled, bool onTheRun = false)
        {
            if (!kicker || !ball) yield break;
            int roundAtStart = _roundId;
            // 2026-08-28, Shaun: "no they dont go back and have a shot after
            // bouncing it they need to kick it on the run". onTheRun skips the
            // whole set-shot ritual below - the walk back, the pause, the run-in
            // - and goes straight to the kick. Everything after it (ball to the
            // boot, the power bar, the flight, the scoring) is shared, so a snap
            // is graded exactly like any other kick.
            if (!onTheRun)
            {
                _message = "Lines up for goal...";
                yield return new WaitForSeconds(shotStartPause);
                if (_roundId != roundAtStart) yield break;
            }

            // Real fix (2026-08-12, Shaun: "it not really evident that the
            // player is able to go back and take there kick"). This used
            // to cut to the wide shot right before the run-IN only — the
            // step-BACK itself played out entirely on the static mark
            // closeup (CutCameraToMarkCloseup, which doesn't track the
            // character), so as the kicker walked backward out of that
            // tight, fixed frame the player was watching an emptying
            // shot, not a "walking back for the kick" read. Cutting wide
            // here instead, before the step-back starts, makes the whole
            // back-then-in run visible in one continuous, stable shot.
            CutCameraForKick(zDir);
            if (!onTheRun)
            {
                float markSpotZ = kicker.position.z;
                yield return RunToZ(kicker, markSpotZ - zDir * shotStepBackDistance, shotStepBackDuration);
                if (_roundId != roundAtStart) yield break;
                yield return new WaitForSeconds(shotSetupPause);
                if (_roundId != roundAtStart) yield break;
                yield return RunToZ(kicker, markSpotZ, shotRunInDuration);
                if (_roundId != roundAtStart) yield break;
            }

            var rightHand = FindDeepChild(kicker, "RightHand");
            var rightFoot = FindDeepChild(kicker, "RightFoot");
            Vector3 handPos = rightHand ? rightHand.position : ball.position;
            Vector3 footPos = rightFoot ? rightFoot.position + Vector3.up * 0.15f : kicker.position + Vector3.up * 0.3f;

            float el = 0f;
            while (el < shotDropDuration)
            {
                if (_roundId != roundAtStart) yield break;
                el += Time.deltaTime;
                float f = Mathf.Clamp01(el / shotDropDuration);
                ball.position = Vector3.Lerp(handPos, footPos, f * f);
                yield return null;
            }

            // Real fix (2026-08-12) — see the field comments on
            // shotPowerRiseDuration above for why this replaced tap-to-
            // aim. Single existing TAP button, one tap, whenever the
            // player chooses. Never hangs: running all the way out
            // without a tap is a real, deliberate miss (max power,
            // way off), not a stall.
            _message = humanControlled ? "Tap when it turns GREEN!" : "Lining up the kick...";
            _shotBarVisible = humanControlled;
            _shotBarValue = 0f;
            bool tapped = false;
            float tapValue = 0f;
            float riseEl = 0f;
            // 2026-08-19, Shaun: "the human kicks at goal for themself and
            // ai" — when Roo (the AI's side) is the one shooting, the
            // human shouldn't have to tap for them. Same randomized-
            // attempt idiom as the ruck tap's _botPressT: aim near the
            // green zone with real spread, a fair but beatable AI shot
            // rather than either an auto-goal or a required human tap.
            float aiTapAt = humanControlled ? 0f
                : Mathf.Clamp01(((shotPowerGreenMin + shotPowerGreenMax) / 2f) + Random.Range(-0.18f, 0.18f)) * shotPowerRiseDuration;
            float powerRise = onTheRun ? snapPowerRiseDuration : shotPowerRiseDuration;
            while (riseEl < powerRise)
            {
                if (_roundId != roundAtStart) { _shotBarVisible = false; yield break; }
                riseEl += Time.deltaTime;
                _shotBarValue = Mathf.Clamp01(riseEl / powerRise);
                if (humanControlled)
                {
                    if (Day1Input.TapDown) { tapped = true; tapValue = _shotBarValue; break; }
                }
                else if (riseEl >= aiTapAt)
                {
                    tapped = true; tapValue = _shotBarValue; break;
                }
                yield return null;
            }
            if (!tapped) tapValue = 1f;
            _shotBarVisible = false;

            bool isGoal = tapped && tapValue >= shotPowerGreenMin && tapValue <= shotPowerGreenMax;
            _message = isGoal ? "GOAL!" : "Off target!";

            Vector3 kickStart = ball.position;
            Vector3 goalCentre = new Vector3(0, kickStart.y, zDir * goalZ);
            if (!isGoal)
            {
                float distFromGreen = tapValue < shotPowerGreenMin ? shotPowerGreenMin - tapValue : tapValue - shotPowerGreenMax;
                float side = tapValue < shotPowerGreenMin ? -1f : 1f;
                goalCentre.x = side * shotMissSpread * Mathf.Clamp01(0.4f + distFromGreen * 2f);
            }

            // Real then, not fake — the outcome (and therefore the exact
            // target) is already decided before the ball leaves the
            // foot, so the arc just flies straight at it. No freeze-then-
            // tween needed here the way the mark's ball needed one.
            el = 0f;
            while (el < shotKickDuration)
            {
                if (_roundId != roundAtStart) yield break;
                el += Time.deltaTime;
                float f = Mathf.Clamp01(el / shotKickDuration);
                float arc = Mathf.Sin(f * Mathf.PI) * shotKickHeight;
                ball.position = Vector3.Lerp(kickStart, goalCentre, f) + Vector3.up * arc;
                yield return null;
            }
            ball.position = goalCentre;

            float holdEl = 0f;
            while (holdEl < shotResultHold)
            {
                if (_roundId != roundAtStart) yield break;
                holdEl += Time.deltaTime;
                yield return null;
            }
        }

        public float speccyLeapRiseDuration = 0.5f;
        public float speccyLeapHeightScale = 4f;

        // Real fix (2026-08-12, Shaun: "the speccy so the forward now
        // starts behind runs up jumps really high on the opponents
        // shoulders and marks it at the peak... you can do some pausing
        // so that really stands out and the jump can be really over the
        // top"). Replaces the earlier plain-RunToZ-then-small-hop combo
        // entirely — a proper speccy is one continuous run-into-leap,
        // not two independent motions that happen to finish at the same
        // time. Owns the forward's ENTIRE motion for this beat (ground
        // run AND leap) rather than running alongside a separate RunToZ
        // call, deliberately: RunToZ and the old MarkJumpRoutine wrote
        // position/localPosition to the same transform independently
        // (RunToZ mid-run when the jump used to fire), which is exactly
        // the kind of two-systems-that-are-supposed-to-agree bug this
        // project keeps getting bitten by. One routine, one writer.
        //
        // Budget: the real-time gap from when this fires to the ball's
        // own peak is fixed by the rest of the kick sequence
        // (kickDropDuration + kickPause + half of kickDuration — the
        // exact value TapBallAway already computes as arriveByPeak) and
        // has to stay that way for "marks it at the peak" to still hold.
        // Spent as: most of it as a real ground run (the "starts behind,
        // runs up" read), the final speccyLeapRiseDuration as the leap
        // itself, so the jump's peak — not just the arrival — lands on
        // the ball's peak.
        //
        // "On the opponents shoulders": during the leap (not the run),
        // X blends from the forward's own lane toward the defender's
        // actual X position, holds there through the peak/pause, then
        // blends back on the way down — reads as leaping past/over the
        // defender rather than a straight-up hop in place.
        System.Collections.IEnumerator SpeccyLeap(Transform forward, Transform defender, float targetZ, float totalDuration)
        {
            if (!forward) yield break;
            int roundAtStart = _roundId;

            Vector3 startPos = forward.position;
            float ownLaneX = startPos.x;
            forward.rotation = Quaternion.Euler(0, targetZ > startPos.z ? 0 : 180, 0);

            // Phase 1: ground run, covering most of the distance —
            // reuses RunToZ's own SmoothStep + Speed-ramp pattern so the
            // running motion itself still matches everywhere else in
            // this file.
            float runDuration = Mathf.Max(0.05f, totalDuration - speccyLeapRiseDuration);
            const float runPortionOfZ = 0.8f;
            Vector3 runEnd = new Vector3(ownLaneX, startPos.y, Mathf.Lerp(startPos.z, targetZ, runPortionOfZ));
            var animator = forward.GetComponentInChildren<Animator>();
            float el = 0f;
            while (el < runDuration)
            {
                if (_roundId != roundAtStart) yield break;
                el += Time.deltaTime;
                float f = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(el / runDuration));
                forward.position = Vector3.Lerp(startPos, runEnd, f);
                if (animator)
                {
                    float rampF = Mathf.Clamp01(Mathf.Min(f, 1f - f) / 0.15f);
                    animator.SetFloat("Speed", 5.5f * rampF, 0.1f, Time.deltaTime);
                }
                yield return null;
            }
            forward.position = runEnd;
            if (animator) animator.SetFloat("Speed", 0f, 0.1f, Time.deltaTime);

            // Phase 2: the leap — really over the top (speccyLeapHeightScale
            // is more than double the ruck's own hop reach on purpose).
            if (animator) animator.enabled = false;
            var leftArm = FindDeepChild(forward, "LeftArm");
            var rightArm = FindDeepChild(forward, "RightArm");
            Quaternion leftStart = leftArm ? leftArm.localRotation : Quaternion.identity;
            Quaternion rightStart = rightArm ? rightArm.localRotation : Quaternion.identity;
            float defenderX = defender ? defender.position.x : ownLaneX;
            Vector3 leapEnd = new Vector3(ownLaneX, startPos.y, targetZ);

            void Pose(float wave, bool zTracks)
            {
                Vector3 p = zTracks ? Vector3.Lerp(runEnd, leapEnd, wave) : leapEnd;
                p.x = Mathf.Lerp(ownLaneX, defenderX, wave);
                p.y += wave * speccyLeapHeightScale;
                forward.position = p;
                if (leftArm) leftArm.localRotation = leftStart * Quaternion.Euler(0, 0, wave * 155f);
                if (rightArm) rightArm.localRotation = rightStart * Quaternion.Euler(0, 0, -wave * 155f);
            }

            el = 0f;
            while (el < speccyLeapRiseDuration)
            {
                if (_roundId != roundAtStart) yield break;
                el += Time.deltaTime;
                Pose(Mathf.Sin(Mathf.Clamp01(el / speccyLeapRiseDuration) * Mathf.PI / 2f), zTracks: true);
                yield return null;
            }
            Pose(1f, zTracks: true);

            // 2026-08-19, Shaun: "marks the ball but the ball is on the
            // ground... only sometimes." Real race: a single frame-timing
            // hitch (GC pause, asset streaming) can push el past BOTH
            // jumpFireAt and markDeadline in the same frame — since
            // StartCoroutine runs synchronously up to its first yield,
            // _markHoldReleased can already be true before the rise above
            // has even properly played out over real frames, collapsing
            // the hold to ~0 duration and dropping straight into the fall.
            // A guaranteed minimum hold makes a hitch unable to skip the
            // peak pose entirely, whatever else happens with the timing.
            float minHoldEl = 0f;
            while (!_markHoldReleased || minHoldEl < minMarkHoldDuration)
            {
                if (_roundId != roundAtStart) yield break;
                minHoldEl += Time.deltaTime;
                yield return null;
            }
            if (_markHoldSucceeded)
            {
                float heldEl = 0f;
                while (heldEl < markCelebrationHold)
                {
                    if (_roundId != roundAtStart) yield break;
                    heldEl += Time.deltaTime;
                    yield return null;
                }
            }

            float fallDur = speccyLeapRiseDuration;
            el = 0f;
            while (el < fallDur)
            {
                if (_roundId != roundAtStart) yield break;
                el += Time.deltaTime;
                Pose(Mathf.Cos(Mathf.Clamp01(el / fallDur) * Mathf.PI / 2f), zTracks: false);
                yield return null;
            }
            forward.position = leapEnd;
            if (leftArm) leftArm.localRotation = leftStart;
            if (rightArm) rightArm.localRotation = rightStart;
            if (animator) animator.enabled = true;
        }

        public float catchPause = 0.5f;
        // 2026-08-21, Shaun: kick-out from fullback felt broken — "the
        // ball randomly goes to the top of the goal posts and then goes
        // to middle... we need to pause at the player kicking in, can
        // really have a decent pause to get it right." The camera was
        // already zooming to the kicker (CutCameraToMarkCloseup) before
        // this beat, but only held for the shared catchPause (0.5s) —
        // nowhere near long enough to actually read a static close-up
        // before the wide kick-arc camera cuts in and the ball moves. A
        // dedicated, much longer pause for this one beat specifically
        // (not touching catchPause's other, faster uses elsewhere in
        // this file) so the kick-out reads as a real, deliberate moment.
        public float kickOutPause = 1.6f;
        // How long the zoomed-in kick motion itself takes (backswing +
        // forward snap) before the camera cuts wide and the ball starts
        // flying — see KickMotion below and its call site.
        public float kickMotionDuration = 0.6f;
        // How far the kick-out actually travels back from the goal
        // square — deliberately short of dead centre (goalZ=20 would be
        // the full traverse), see the call site's own comment.
        public float kickOutDistance = 12f;
        // Real fix (2026-08-12, Shaun: "run of a bit far", then "can be a
        // slower like 4 step run"). 14 units / 1.8s was a full sprint pace
        // with no acceleration — cut down to a short, deliberate few-step
        // jog instead. runSpeed on AFLPlayer (the six-player game's own
        // player script) calibrates its Run animation state at 7 units/sec
        // and Walk at half that (3.5) — this run now moves at ~4 units/sec,
        // matching Walk-ish pace rather than a full Run blend.
        public float runDistance = 6f;
        // How fast the carrier drifts across the ground while steering, and how
        // far from the corridor he is allowed to get. Wide enough to change the
        // angle of the shot, not so wide he ends up off the field.
        public float steerSpeed = 4.5f;
        public float steerLimit = 7f;
        // Carried across the legs of a run so steering accumulates, and reset at
        // the start of each round rather than each leg.
        float _steerOffsetX;
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
        // 2026-08-28, Shaun: "add a scene in the centre were the rover sprints
        // out of the centre has a bounce and kicks a snap on the run."
        //
        // Reuses RunStraight for both legs rather than duplicating its animator
        // and hand-tracking work - it already drives the Speed parameter and
        // pins the ball to the real hand bones, and a second copy of that would
        // drift out of sync exactly like every other duplicate in this file has.
        public float centreBurstChance = 0.45f;
        public float bounceDuration = 0.55f;

        // 2026-08-28, Shaun: "when the palyer in the middle grabs it they handball
        // to a player running past who keeps running and then goes in and kicks a
        // goal".
        //
        // This also avoids the real pull back toward the centre: the rovers are
        // spawned at z = +-1.8, the centre bounce itself, and TapBallAway lerps
        // the ball to whoever is carrying it. Handballing forward to the clearer
        // (spawned deep, at z = +-13) moves the ball AWAY from the middle instead
        // of dragging it back there.
        // 2026-08-28, Shaun: "now we need the rovers to be able to chase out of
        // the middle". The opposition rover pursues the carrier rather than
        // standing at the bounce watching him go - he trails behind and slightly
        // to the side, so the break out of the centre reads as escaping someone.
        // Deliberately no tackle: this is pursuit, which Shaun asked for
        // explicitly ("have the rover chase with no tackle").
        public float chaseTrail = 2.2f;
        public float chaseSpeed = 5.2f;
        public float handballChance = 0.35f;

        System.Collections.IEnumerator ChaseCarrier(Transform chaser, Transform carrier, float zDir)
        {
            if (!chaser || !carrier) yield break;
            int roundAtStart = _roundId;
            var animator = chaser.GetComponentInChildren<Animator>();
            while (_roundId == roundAtStart)
            {
                Vector3 target = new Vector3(
                    carrier.position.x + (zDir > 0f ? 0.9f : -0.9f),
                    chaser.position.y,
                    carrier.position.z - zDir * chaseTrail);
                chaser.position = Vector3.MoveTowards(chaser.position, target, chaseSpeed * Time.deltaTime);
                chaser.rotation = Quaternion.Euler(0f, zDir > 0f ? 0f : 180f, 0f);
                if (animator) animator.SetFloat("Speed", 5.5f);
                yield return null;
            }
            if (animator) animator.SetFloat("Speed", 0f);
        }
        public float handballDuration = 0.4f;

        System.Collections.IEnumerator HandballAndRun(Transform rover, float zDir, bool humanControlled)
        {
            Transform runner = zDir > 0f ? crocRunner : rooRunner;
            if (!runner) runner = zDir > 0f ? crocClearer : rooClearer;   // pre-runner scenes
            if (!rover || !runner || !ball) yield break;
            int roundAtStart = _roundId;

            // The runner comes past, ahead of the rover, going the same way.
            runner.position = new Vector3(runner.position.x, runner.position.y, rover.position.z + zDir * 3.5f);
            runner.rotation = Quaternion.Euler(0f, zDir > 0f ? 0f : 180f, 0f);

            CutCameraToDefault();
            StartCoroutine(ChaseCarrier(zDir > 0f ? rooRover : crocRover, rover, zDir));
            _message = "Handballs to a runner!";
            var fromHand = FindDeepChild(rover, "RightHand");
            var toHand = FindDeepChild(runner, "LeftHand");
            Vector3 from = fromHand ? fromHand.position : rover.position + Vector3.up;
            float el = 0f;
            while (el < handballDuration)
            {
                if (_roundId != roundAtStart) yield break;
                el += Time.deltaTime;
                float f = Mathf.Clamp01(el / handballDuration);
                Vector3 to = toHand ? toHand.position : runner.position + Vector3.up;
                // flat and quick, the way a handball travels
                ball.position = Vector3.Lerp(from, to, f) + Vector3.up * Mathf.Sin(f * Mathf.PI) * 0.25f;
                yield return null;
            }

            // 2026-08-28, Shaun: "thats more of a long shopt" - two legs left him
            // kicking from well out. A third carries him into range.
            _message = "Runs it into the forward line!";
            for (int leg = 0; leg < 3; leg++)
            {
                yield return RunStraight(runner, zDir, steerable: zDir > 0f);
                if (_roundId != roundAtStart) yield break;
            }
            yield return PlayOnSnap(runner, zDir, humanControlled);
        }

        System.Collections.IEnumerator BurstBounceSnap(Transform rover, float zDir, bool humanControlled)
        {
            if (!rover || !ball) yield break;
            int roundAtStart = _roundId;

            // 2026-08-28, Shaun: "far out now the camera problems again". These
            // scenes are entered straight from the ruck contest's own close
            // framing, so without this they inherit that pivot and the run plays
            // out off-frame - the same drift the chain hops were fixed for.
            CutCameraToDefault();
            StartCoroutine(ChaseCarrier(zDir > 0f ? rooRover : crocRover, rover, zDir));
            _message = "Breaks out of the centre!";
            yield return RunStraight(rover, zDir, steerable: zDir > 0f);
            if (_roundId != roundAtStart) yield break;

            // The bounce: ball out of the hands, down to ground, back up.
            _message = "Bounces it...";
            var hand = FindDeepChild(rover, "RightHand");
            Vector3 from = hand ? hand.position : rover.position + Vector3.up;
            float el = 0f;
            while (el < bounceDuration)
            {
                if (_roundId != roundAtStart) yield break;
                el += Time.deltaTime;
                float f = Mathf.Clamp01(el / bounceDuration);
                Vector3 h = hand ? hand.position : rover.position + Vector3.up;
                // down to the turf and back to the hand, travelling with the run
                float dip = Mathf.Sin(f * Mathf.PI);
                Vector3 at = Vector3.Lerp(from, h, f);
                ball.position = new Vector3(at.x, Mathf.Lerp(at.y, groundY, dip), at.z + zDir * dip * 0.8f);
                yield return null;
            }

            yield return RunStraight(rover, zDir, steerable: zDir > 0f);
            if (_roundId != roundAtStart) yield break;
            yield return PlayOnSnap(rover, zDir, humanControlled);
        }

        System.Collections.IEnumerator PlayOnSnap(Transform kicker, float zDir, bool humanControlled)
        {
            if (!kicker || !ball) yield break;
            int roundAtStart = _roundId;
            _message = "Snaps for goal on the run!";
            CutCameraForKick(zDir, kicker.position.z);
            yield return TakeShotAtGoal(kicker, zDir, humanControlled, onTheRun: true);
        }

        System.Collections.IEnumerator RunStraight(Transform t, float zDir, bool steerable = false)
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
                Vector3 along = Vector3.Lerp(start, end, smoothF);
                if (steerable)
                {
                    // Steering only moves him ACROSS the ground; the run itself
                    // still carries him forward on its own curve. Accumulated so
                    // it persists into the kick, which is the point - where you
                    // end up decides the angle you shoot from.
                    _steerOffsetX = Mathf.Clamp(
                        _steerOffsetX + Day1Input.SteerAxis * steerSpeed * Time.deltaTime,
                        -steerLimit, steerLimit);
                }
                along.x += _steerOffsetX;
                t.position = along;
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
        // 2026-08-28: a spoil is a REACH that ends in a short jab. Reusing the
        // ruck's HopRoutine here reads as an uppercut, because that one sweeps
        // BOTH arms up through 155 degrees to tap a ball from underneath.
        void SpoilPunch(Transform t)
        {
            if (t) StartCoroutine(SpoilPunchRoutine(t));
        }

        System.Collections.IEnumerator SpoilPunchRoutine(Transform t)
        {
            Vector3 start = t.localPosition;
            var leftArm = FindDeepChild(t, "LeftArm");
            var rightArm = FindDeepChild(t, "RightArm");
            Quaternion leftStart = leftArm ? leftArm.localRotation : Quaternion.identity;
            Quaternion rightStart = rightArm ? rightArm.localRotation : Quaternion.identity;
            var animator = t.GetComponentInChildren<Animator>();
            if (animator) animator.enabled = false;

            float dur = hopDuration;
            float el = 0f;
            while (el < dur)
            {
                el += Time.deltaTime;
                float f = el / dur;
                float wave = Mathf.Sin(f * Mathf.PI);
                t.localPosition = start + Vector3.up * wave * contestLeapHeight;
                // both arms reach up with the leap - same idiom as the forward's mark
                float reach = wave * 145f;   // just under his 155: spoiling, not marking
                // a short jab at the top, not a wind-up
                float jab = Mathf.Sin(Mathf.Clamp01((f - 0.35f) / 0.3f) * Mathf.PI) * 30f;
                if (rightArm) rightArm.localRotation = rightStart * Quaternion.Euler(0, 0, -(reach + jab));
                if (leftArm)  leftArm.localRotation  = leftStart  * Quaternion.Euler(0, 0,   reach);
                yield return null;
            }
            t.localPosition = start;
            if (leftArm) leftArm.localRotation = leftStart;
            if (rightArm) rightArm.localRotation = rightStart;
            if (animator) animator.enabled = true;
        }

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
            float heightScale = reachesBall ? contestLeapHeight : 0.5f;
            Vector3 towardCentre = reachesBall
                ? new Vector3(-Mathf.Sign(start.x) * 0.55f, 0, 0)
                : Vector3.zero;
            float armAngle = reachesBall ? contestArmAngle : 60f;

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

        // 2026-08-21, Shaun (live playtest): "the ball randomly goes to
        // the top of the goal posts and then goes to middle... make sure
        // you see them kick the ball out." Real root cause — there was
        // never any kicking MOTION anywhere in this file, only jump/hop
        // animations (HopRoutine, NormalMarkHop, SpeccyLeap). The ball
        // just moved on its own while the character stood static, which
        // is exactly why a kick read as "random" rather than caused by
        // the player. Same technique as HopRoutine (direct bone
        // rotation, Animator disabled for the duration, no clip needed)
        // but driving RightUpLeg instead of the arms — a real backswing
        // then forward snap, timed so the foot is at full forward
        // extension right when the ball actually starts moving (see the
        // call site — this is started slightly before the ball's own
        // kickOutEl loop, not at the same instant).
        public float kickMotionBackswingFrac = 0.35f;
        public float kickMotionLegAngle = 65f;
        System.Collections.IEnumerator KickMotion(Transform t, float duration)
        {
            if (!t) yield break;
            var upLeg = FindDeepChild(t, "RightUpLeg");
            if (!upLeg) yield break;
            Quaternion legStart = upLeg.localRotation;
            var animator = t.GetComponentInChildren<Animator>();
            if (animator) animator.enabled = false;

            float el = 0f;
            while (el < duration)
            {
                el += Time.deltaTime;
                float f = Mathf.Clamp01(el / duration);
                // Backswing (leg draws back) for the first fraction, then
                // a fast forward snap through the rest — a real kick
                // isn't a symmetric wave like the hop's reach gesture,
                // the forward snap is the sharp, fast part.
                float angle;
                if (f < kickMotionBackswingFrac)
                {
                    angle = Mathf.Lerp(0f, -25f, f / kickMotionBackswingFrac);
                }
                else
                {
                    float snapF = (f - kickMotionBackswingFrac) / (1f - kickMotionBackswingFrac);
                    angle = Mathf.Lerp(-25f, kickMotionLegAngle, Mathf.Sin(snapF * Mathf.PI * 0.5f));
                }
                upLeg.localRotation = legStart * Quaternion.Euler(angle, 0, 0);
                yield return null;
            }
            upLeg.localRotation = legStart;
            if (animator) animator.enabled = true;
        }

        // 2026-08-21 — KickMotion, but the ball rides the boot for the
        // duration instead of sitting wherever it landed after the
        // previous kick. Split out rather than folded into KickMotion
        // itself because the centre-clearance kick calls that one with
        // the ball already in flight and must not have it yanked back to
        // the kicker's foot mid-arc. Same idiom as MarkCatchRoutine
        // tracking the forward's real hand bone rather than a guessed
        // nearby point.
        System.Collections.IEnumerator KickMotionWithBall(Transform t, Transform boot, float duration)
        {
            int roundAtStart = _roundId;
            var inner = StartCoroutine(KickMotion(t, duration));
            float el = 0f;
            while (el < duration)
            {
                if (_roundId != roundAtStart) yield break;
                el += Time.deltaTime;
                if (ball && boot) ball.position = boot.position;
                yield return null;
            }
            yield return inner;
        }

        // 2026-08-19: the normal (non-speccy) mark's jump — used for both
        // forward and defender. Reuses HopRoutine's own reach numbers
        // (heightScale 1.65, armAngle 155 — the same math kickHeightNormal
        // is tuned against) but, critically, does NOT run on a fixed
        // clock like HopRoutine does. HopRoutine finishes and returns to
        // the ground on its own schedule regardless of when the mark
        // actually resolves — that's exactly what caused "says MARK! but
        // the ball is on the ground" (markDeadline could land after the
        // fixed-duration hop had already landed and returned). This rises
        // then HOLDS at peak until KickAway sets _markHoldReleased, same
        // pattern SpeccyLeap already uses above — no clock to race
        // against, so the outcome and the pose can never drift apart.
        System.Collections.IEnumerator NormalMarkHop(Transform t)
        {
            if (!t) yield break;
            int roundAtStart = _roundId;
            Vector3 start = t.localPosition;
            float heightScale = contestLeapHeight;
            const float armAngle = 155f;

            var leftArm = FindDeepChild(t, "LeftArm");
            var rightArm = FindDeepChild(t, "RightArm");
            Quaternion leftStart = leftArm ? leftArm.localRotation : Quaternion.identity;
            Quaternion rightStart = rightArm ? rightArm.localRotation : Quaternion.identity;
            var animator = t.GetComponentInChildren<Animator>();
            if (animator) animator.enabled = false;

            void Pose(float wave)
            {
                t.localPosition = start + Vector3.up * wave * heightScale;
                if (leftArm) leftArm.localRotation = leftStart * Quaternion.Euler(0, 0, wave * armAngle);
                if (rightArm) rightArm.localRotation = rightStart * Quaternion.Euler(0, 0, -wave * armAngle);
            }

            float el = 0f;
            while (el < normalMarkHopRiseDuration)
            {
                if (_roundId != roundAtStart) yield break;
                el += Time.deltaTime;
                Pose(Mathf.Sin(Mathf.Clamp01(el / normalMarkHopRiseDuration) * Mathf.PI / 2f));
                yield return null;
            }
            Pose(1f);

            // See SpeccyLeap's identical guard above for why this can't
            // just be "while (!_markHoldReleased)" — a frame-timing hitch
            // can make that already true before the rise even finishes.
            float minHoldEl = 0f;
            while (!_markHoldReleased || minHoldEl < minMarkHoldDuration)
            {
                if (_roundId != roundAtStart) yield break;
                minHoldEl += Time.deltaTime;
                yield return null;
            }

            el = 0f;
            while (el < normalMarkHopRiseDuration)
            {
                if (_roundId != roundAtStart) yield break;
                el += Time.deltaTime;
                Pose(Mathf.Cos(Mathf.Clamp01(el / normalMarkHopRiseDuration) * Mathf.PI / 2f));
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

            // Real fix (2026-08-12, Shaun: "no red light system and it
            // chaotic no idea when to push buttons what to do" / "even a
            // power button visible"). Explicit, unmissable cue: a bar
            // that rises once, red everywhere except a GREEN band drawn
            // right on the track marking the actual scoring window — the
            // player can see it coming, not just react blind. Same "the
            // cue is visual, not a hidden number" principle as every
            // other timing mechanic in this file.
            if (_shotBarVisible)
            {
                int barW = Mathf.RoundToInt(Screen.width * 0.6f);
                int barH = Mathf.RoundToInt(Screen.height * 0.07f);
                int barX = (Screen.width - barW) / 2;
                int barY = Mathf.RoundToInt(Screen.height * 0.62f);

                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(new Rect(barX - 6, barY - 6, barW + 12, barH + 12), Texture2D.whiteTexture);

                GUI.color = new Color(0.75f, 0.2f, 0.2f, 0.9f);
                GUI.DrawTexture(new Rect(barX, barY, barW, barH), Texture2D.whiteTexture);

                GUI.color = new Color(0.25f, 0.8f, 0.3f, 0.95f);
                float greenX = barX + shotPowerGreenMin * barW;
                float greenW = (shotPowerGreenMax - shotPowerGreenMin) * barW;
                GUI.DrawTexture(new Rect(greenX, barY, greenW, barH), Texture2D.whiteTexture);

                bool inGreen = _shotBarValue >= shotPowerGreenMin && _shotBarValue <= shotPowerGreenMax;
                GUI.color = inGreen ? Color.white : new Color(1f, 0.9f, 0.3f, 1f);
                float markerX = barX + Mathf.Clamp01(_shotBarValue) * barW;
                GUI.DrawTexture(new Rect(markerX - 4, barY - 10, 8, barH + 20), Texture2D.whiteTexture);

                GUI.color = Color.white;
            }
        }
    }

    // Minimal input wrapper, deliberately separate from AFLInput (the
    // six-player game's input) — one button only, per issue #6.
    public static class Day1Input
    {
        // -1 left, +1 right, 0 straight. Set by the WebGL template while a
        // pointer is held, and by the keyboard for desktop play.
        public static float Steer;
        public static float SteerAxis
        {
            get
            {
                float k = 0f;
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) k -= 1f;
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) k += 1f;
                return Mathf.Clamp(k + Steer, -1f, 1f);
            }
        }
        public static bool TouchTapDown;
        // 2026-08-19, Shaun: relying on "remember to press spacebar, not
        // click" was fragile for real testing — a direct mouse/touch
        // click on the canvas is the natural interaction and should just
        // work, not only the external app-bridge (TapPressed) or the
        // spacebar fallback.
        public static bool TapDown => Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0) || TouchTapDown;
        internal static void ClearOneShot() { TouchTapDown = false; }
    }

    public class Day1TouchBridge : MonoBehaviour
    {
        void LateUpdate() { Day1Input.ClearOneShot(); }
        public void TapPressed(string _) { Day1Input.TouchTapDown = true; }
        // 2026-08-28, Shaun: "i wonder if we can make that button like unicorn
        // surf 3d and you can easily stear it to move around a bit more could
        // actually chnage the dynamics of the game". Same scheme that game uses:
        // hold the left or right half of the screen to steer, tap to act.
        public void SetSteer(string v)
        {
            float f;
            Day1Input.Steer = float.TryParse(v, out f) ? Mathf.Clamp(f, -1f, 1f) : 0f;
        }
    }
}
