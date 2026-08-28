using UnityEngine;
using Unity.Cinemachine;

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

        // 2026-08-28, Shaun: "lets do it with cinemashine." One vcam per shot
        // type; the CinemachineBrain on the Main Camera blends between them.
        // Every CutCameraX below keeps its ORIGINAL framing maths untouched
        // and simply writes it onto the relevant vcam instead of onto the
        // camera itself, then raises that vcam's priority.
        //
        // Null-safe throughout: if these are unassigned (an older scene, or a
        // build where the brain failed to attach) every cut falls back to the
        // direct transform write it used before, so the camera degrades to the
        // previous behaviour rather than freezing on one shot.
        public CinemachineCamera vcamDefault;
        public CinemachineCamera vcamKick;
        public CinemachineCamera vcamKickOut;
        public CinemachineCamera vcamCloseup;
        public CinemachineCamera vcamGoalPos;
        public CinemachineCamera vcamGoalNeg;

        bool UsingCinemachine => vcamDefault && vcamKick && vcamKickOut && vcamCloseup;
        bool HasGoalCams => vcamGoalPos && vcamGoalNeg;

        // 2026-08-28, Shaun: "make the game as fun and entertaining as
        // posibblle liek afl but exagarrated". The single most football-looking
        // shot there is: behind the goals, watching the kick come at you
        // through the big sticks. Used only while the ball is actually in
        // flight at goal, so it stays an event rather than a default view.
        public float goalCamBehind = 9f;
        public float goalCamHeight = 3.2f;
        void CutCameraBehindGoals(float zDir)
        {
            if (!UsingCinemachine || !HasGoalCams) return;
            var vc = zDir > 0f ? vcamGoalPos : vcamGoalNeg;
            vc.transform.position = new Vector3(0f, goalCamHeight, zDir * (goalZ + goalCamBehind));
            vc.transform.LookAt(new Vector3(0f, 1.6f, zDir * (goalZ - 6f)));
            vcamDefault.Priority.Value = 0;
            vcamKick.Priority.Value = 0;
            vcamKickOut.Priority.Value = 0;
            vcamCloseup.Priority.Value = 0;
            vcamGoalPos.Priority.Value = ReferenceEquals(vcamGoalPos, vc) ? 20 : 0;
            vcamGoalNeg.Priority.Value = ReferenceEquals(vcamGoalNeg, vc) ? 20 : 0;
        }

        void ActivateVcam(CinemachineCamera live)
        {
            if (!UsingCinemachine) return;
            vcamDefault.Priority.Value = ReferenceEquals(vcamDefault, live) ? 20 : 0;
            vcamKick.Priority.Value = ReferenceEquals(vcamKick, live) ? 20 : 0;
            vcamKickOut.Priority.Value = ReferenceEquals(vcamKickOut, live) ? 20 : 0;
            vcamCloseup.Priority.Value = ReferenceEquals(vcamCloseup, live) ? 20 : 0;
            // Must clear these too - otherwise a goal cam left at 20 outranks
            // whatever shot the next beat asks for and the camera sticks
            // behind the posts for the rest of the game.
            if (vcamGoalPos) vcamGoalPos.Priority.Value = 0;
            if (vcamGoalNeg) vcamGoalNeg.Priority.Value = 0;
        }
        Coroutine _defenderRunToZ;
        Vector3 _camDefaultPos;
        Quaternion _camDefaultRot;
        // Must match Day1BuildScript's BuildGoalPosts z position.
        public float goalZ = 20f;
        public float kickCamSide = 9f;
        public float kickCamHeight = 4.5f;
        // Real fix (2026-08-12, Shaun: "maybe pause them in mid air when
        // they have taken the mark" / "just brief pause"). Held once the
        // mark-jump's rise reaches its peak, only when it's a genuine
        // mark — a spill falls straight back down instead.
        public float markCelebrationHold = 0.3f;
        bool _markHoldReleased;
        bool _markHoldSucceeded;

        public float throwDuration = 2.6f;
        public float peakHeight = 2.8f;
        public float groundY = 1.0f;
        public float hopDuration = 0.45f;

        // 2026-08-28, Shaun: "is it possible for the ruck contest to be a bit
        // more of a run in and jump", then "ruck is more than a hop its a full
        // leap". Both true of the code as it stood: the rucks started 1.1m
        // apart already under the ball and played a standing hop at height
        // scale 1.65, against the speccy's real leap at 4.0 - about 40% of a
        // leap, and no run at all.
        //
        // The run-in happens in Update during the throw-up (not in HopRoutine)
        // so the leap's own timing is untouched. That timing is load-bearing:
        // _hopFireAt is the ball's true visual peak and the whole tap contest
        // is judged against it.
        public float ruckRunInDistance = 2.9f;

        // Leap height is DERIVED, never hand-tuned, because the previous
        // hand-tuned value is exactly what would break here. 1.65 was measured
        // (ArmAngleCheck.cs) to put the hands at 2.95 against a ball frozen at
        // 3.1 - raise the throw without rescaling the leap and the rucks paw
        // downward at a ball above them, which reads worse than the hop did.
        // Deriving it means peakHeight can be tuned freely and the hands still
        // meet the ball by construction.
        public float ruckStandHandY = 1.48f;    // measured: hands, arms raised, standing
        public float ruckRisePerScale = 0.891f; // measured: scale 1.65 -> hands 2.95
        float RuckLeapScale =>
            Mathf.Max(1.65f, ((groundY + peakHeight) - ruckStandHandY) / ruckRisePerScale);

        Vector3 _crocRuckIn, _rooRuckIn;   // where each ruck contests
        bool _ruckRunInReady;
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
        public float perfectWindow = 0.55f;   // 10% easier, 2026-08-28
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
        GUIStyle _scoreStyle;
        GUIStyle _roundStyle;

        // 2026-08-21, Shaun: "we could probably hook up a scoreboard now" —
        // real running match score, not just narrated text. Both current
        // scoring events (a goal in TakeShotAtGoal, a rushed behind in the
        // spoil branch above) already exist as messages but never actually
        // tallied anything. AddScore takes the SAME crocsInPossession/
        // humanControlled value already threaded through every contest
        // (they're the same boolean by identity throughout this file) —
        // whichever team was attacking in that specific contest is who a
        // goal or rushed behind is credited to, same as real AFL scoring
        // (a rushed behind scores for the attacking team even though the
        // defender is the one who touched it through).
        int _crocScore;
        int _rooScore;
        void AddScore(bool crocsInPossession, int points)
        {
            if (crocsInPossession) _crocScore += points; else _rooScore += points;
        }

        // 2026-08-21, Shaun: "can we set up 3 minute quarters" — real match
        // structure on top of the score above. The clock only ever advances
        // the round-restart decision at the SAME point Update() already
        // waited for a round to fully finish (_sequenceComplete) — never
        // mid-contest — so time running out doesn't cut a kick or a mark
        // off partway through, same "let the sequence complete" discipline
        // as everything else _roundId-gated in this file.
        public float quarterDuration = 180f; // 3 real minutes
        public float quarterBreakPause = 3f;
        int _quarter = 1;
        float _quarterTimeRemaining;
        bool _matchOver;
        bool _handlingRoundEnd;

        // 2026-08-22, Shaun: "lets have a wildcard round finals and a grand
        // final just make up teams apart from mount dunned cats and well
        // add all the players and map more logistics later." Scoped
        // exactly as asked — made-up OPPONENT NAMES for a 3-round finals
        // series, nothing else. Real rosters/new character models for
        // these teams are explicitly deferred, not built here: Croc is
        // always the home team (Mount Duneed Cats), Roo is always the
        // away side, same as every contest already in this file — only
        // the DISPLAY NAME attached to "Roo" changes each round. No new
        // gameplay, no new characters, just relabeling the existing
        // Croc-vs-Roo match with finals-series context.
        const string HomeTeamLong = "Mount Duneed Cats";
        const string HomeTeamShort = "CATS";
        struct Fixture { public string round; public string awayLong; public string awayShort; }
        readonly Fixture[] _fixtures = new Fixture[]
        {
            new Fixture { round = "Wildcard Round", awayLong = "Bannockburn Bulldogs", awayShort = "BULLDOGS" },
            new Fixture { round = "Finals",         awayLong = "Torquay Tigers",       awayShort = "TIGERS"   },
            new Fixture { round = "Grand Final",    awayLong = "Ocean Grove Sharks",   awayShort = "SHARKS"   },
        };
        int _fixtureIndex;
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
                // With a brain attached the camera transform is brain-driven,
                // so the resting shot is defined by vcamDefault, not by
                // whatever the camera happens to read on the first frame.
                _camDefaultPos = vcamDefault ? vcamDefault.transform.position : _mainCam.transform.position;
                _camDefaultRot = vcamDefault ? vcamDefault.transform.rotation : _mainCam.transform.rotation;
            }
            _quarterTimeRemaining = quarterDuration;
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
                if (UsingCinemachine) ActivateVcam(vcamDefault);
                else
                {
                    _mainCam.transform.position = _camDefaultPos;
                    _mainCam.transform.rotation = _camDefaultRot;
                }
            }
            // Push the rucks out to their run-up marks. Done here rather
            // than in the scene so their resting/contest position stays the
            // measured one every other beat already depends on.
            _ruckRunInReady = false;
            if (crocVisual && rooVisual)
            {
                _crocRuckIn = crocVisual.localPosition;
                _rooRuckIn = rooVisual.localPosition;
                crocVisual.localPosition = _crocRuckIn + new Vector3(-ruckRunInDistance, 0f, 0f);
                rooVisual.localPosition = _rooRuckIn + new Vector3(ruckRunInDistance, 0f, 0f);
                _ruckRunInReady = true;
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

        System.Collections.IEnumerator HandleRoundEnd()
        {
            if (_quarterTimeRemaining <= 0f)
            {
                if (_quarter >= 4)
                {
                    Fixture f = _fixtures[_fixtureIndex];
                    bool draw = _crocScore == _rooScore;
                    bool homeWon = _crocScore > _rooScore;
                    bool isGrandFinal = _fixtureIndex >= _fixtures.Length - 1;

                    if (draw)
                    {
                        // Logistics deferred per Shaun's own scoping — a
                        // finals draw would need a real replay/extra-time
                        // rule to resolve. Not built yet: hold here rather
                        // than silently picking a fake winner.
                        _matchOver = true;
                        _message = "FULL TIME! " + f.round.ToUpper() + " — IT'S A DRAW!";
                        _handlingRoundEnd = false;
                        yield break;
                    }

                    string winnerName = homeWon ? HomeTeamLong : f.awayLong;
                    if (isGrandFinal)
                    {
                        _matchOver = true;
                        _message = winnerName.ToUpper() + " ARE PREMIERS!";
                        _handlingRoundEnd = false;
                        yield break;
                    }

                    Fixture next = _fixtures[_fixtureIndex + 1];
                    _message = winnerName + " win the " + f.round + "! Next: " + next.round + " vs " + next.awayLong;
                    yield return new WaitForSeconds(quarterBreakPause * 2f);
                    _fixtureIndex++;
                    _quarter = 1;
                    _crocScore = 0;
                    _rooScore = 0;
                    _quarterTimeRemaining = quarterDuration;
                    _handlingRoundEnd = false;
                    BeginThrow();
                    yield break;
                }
                _message = "End of Quarter " + _quarter + "!";
                _quarter++;
                yield return new WaitForSeconds(quarterBreakPause);
                _quarterTimeRemaining = quarterDuration;
            }
            _handlingRoundEnd = false;
            BeginThrow();
        }

        void Update()
        {
            if (!_matchOver) _quarterTimeRemaining -= Time.deltaTime;

            if (_resolved)
            {
                // Real fix (2026-08-12) — adding the catch-pause-then-run
                // sequence made the full post-resolve chain (tap flight +
                // catch pause + run) longer than the old fixed 2.2s reset
                // window, which would have reset the scene mid-run. The
                // reset now waits on _sequenceComplete (set at the true end
                // of TapBallAway) rather than a timer that predates the run
                // existing at all.
                //
                // 2026-08-21 — this is also the ONLY point that ever
                // starts a new round, so it's the one safe place to check
                // whether the quarter clock has run out too: never mid-
                // contest, only once the previous one has genuinely
                // finished playing out.
                if (_sequenceComplete && !_handlingRoundEnd && Time.time - _resolvedAt > 1.2f)
                {
                    _handlingRoundEnd = true;
                    StartCoroutine(HandleRoundEnd());
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
                // Run in so they arrive on the contest spot exactly as the
                // ball reaches its peak and the leap fires. SmoothStep, not
                // linear, so they accelerate off the mark and settle into the
                // contest instead of sliding at a constant rate.
                if (_ruckRunInReady && crocVisual && rooVisual)
                {
                    float runF = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t / Mathf.Max(0.01f, _hopFireAt)));
                    crocVisual.localPosition = Vector3.Lerp(
                        _crocRuckIn + new Vector3(-ruckRunInDistance, 0f, 0f), _crocRuckIn, runF);
                    rooVisual.localPosition = Vector3.Lerp(
                        _rooRuckIn + new Vector3(ruckRunInDistance, 0f, 0f), _rooRuckIn, runF);
                    var ca = crocVisual.GetComponentInChildren<Animator>();
                    var ra = rooVisual.GetComponentInChildren<Animator>();
                    float spd = runF < 0.97f ? 1f : 0f;
                    if (ca && ca.enabled) ca.SetFloat("Speed", spd);
                    if (ra && ra.enabled) ra.SetFloat("Speed", spd);
                }
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
            // 2026-08-23, Shaun: "when the rover gets the ball the other
            // rover can chase the player with the ball if they catch them
            // it causes the kick to fall short of the forward. then its
            // stal now to kick to forward for normal mark." The opposing
            // rover (still back at the ruck contest, not otherwise doing
            // anything during this beat) chases alongside the ball-
            // carrier's own run. Decide-then-perform (this file's own
            // established principle, same idiom defenderSpoilT/_botPressT
            // already use for a fair-but-beatable AI contest) — the catch
            // outcome is rolled before the run starts, not derived from
            // simulating two independent movements and comparing
            // positions after the fact. Caught -> reuses the exact
            // "falls short" scene already built and verified tonight
            // (ShortKickLanding, via a per-round effective undershoot
            // below); not caught -> completely unchanged normal-distance
            // kick to a normal mark, same as before this feature existed.
            Transform chaser = crocsInPossession ? rooRover : crocRover;
            bool caughtByChaser = chaser && Random.value < chaseCatchChance;
            // 2026-08-28, Shaun: "the run and tackle just needs to be a bit
            // more natural". The chaser used to set off from wherever it was
            // standing and run its own parallel lane, so at the moment of the
            // catch the two were merely near each other - the arms read as a
            // tackle but the contact never did. Start it on the carrier's own
            // line and a couple of metres back, so it is genuinely running
            // them down from behind and finishes on top of them.
            //
            // Only when the catch is actually going to happen: an uncaught
            // chase should still look like a chase that was beaten, not like
            // one that teleported onto the carrier and then let them go.
            if (chaser && rover && caughtByChaser)
            {
                chaser.position = new Vector3(
                    rover.position.x + 0.7f,
                    chaser.position.y,
                    rover.position.z - runDir * 2.4f);
                chaser.rotation = rover.rotation;
            }
            if (chaser) StartCoroutine(RunStraight(chaser, runDir, carriesBall: false));
            yield return RunStraight(rover, runDir);
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
            _message = caughtByChaser
                ? (crocsInPossession ? "Caught by the Roo!" : "Caught by the Croc!")
                : (crocsInPossession ? "Crocs run it out!" : "Roos run it out!");
            if (caughtByChaser) yield return TackleGrab(chaser, rover);

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
            // Single source of truth for this kick's real distance — see
            // shortKickUndershoot's own comment above. Computed once here,
            // used for peakZ (the forward/defender run target) below, and
            // passed explicitly into KickAway so the ball's own flight
            // can't drift out of sync with where the forward actually is.
            // 2026-08-23 — the chase's own catch outcome (caughtByChaser,
            // above) now feeds this too: shortKickUndershoot alone stays 0
            // (the "no change from normal" default, see its own comment),
            // real per-round undershoot instead comes from getting
            // physically caught by the chasing rover, via
            // chaseTackleUndershoot below.
            float effectiveKickDistance = Mathf.Max(2f, kickDistance - shortKickUndershoot - (caughtByChaser ? chaseTackleUndershoot : 0f));
            // Clamped for the same reason rover.position.z was clamped
            // above — this adds another effectiveKickDistance*0.5 (8
            // units at full distance) beyond rover's already-clamped
            // position, which alone can still land outside the field on a
            // deep chain hop.
            float peakZ = Mathf.Clamp(rover.position.z + runDir * effectiveKickDistance * 0.5f, -(goalZ - 2f), goalZ - 2f);
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
            yield return KickAway(rover, runDir, forward, defender, isSpeccy, crocsInPossession, chainDepth, effectiveKickDistance);
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
        // 2026-08-23, Shaun: "set a scene up... where the kick into the
        // forward line falls short of the forward." kickDistance alone
        // can't do this safely — it already feeds BOTH the forward/
        // defender's run target (peakZ, TapBallAway below) AND the ball's
        // own flight/freeze point (KickAway), computed independently in
        // two separate places from the same constant. They've only ever
        // stayed in sync because kickDistance never varied — exactly the
        // "one fact written in two places" trap this file's own header
        // warns about, just not yet triggered. shortKickUndershoot is
        // read ONCE in TapBallAway to build effectiveKickDistance, which
        // is then passed explicitly into KickAway as a parameter — a
        // single source of truth, not two formulas that happen to agree
        // today. 0 = no change from normal. Was set to 5 for the short-
        // kick scene's own build/test session so it fired deterministically
        // every round instead of being a rare thing to wait for — now that
        // BOTH that scene and the follow-on play-on-a-mark scene are built
        // and need testing, a permanently-forced short kick makes the
        // NORMAL full-distance mark (which play-on hangs off) unreachable.
        // Back to 0 (real normal-distance kicks, mark/play-on reachable
        // again) until Shaun decides how often a kick should actually fall
        // short in real play — that's a genuine game-balance call, not
        // something to silently pick a probability for here.
        public float shortKickUndershoot = 0f;
        // 2026-08-23 — the chase mechanic's own tunables (see
        // caughtByChaser in TapBallAway). chaseCatchChance is deliberately
        // NOT near-certain either way: "if they catch them" implies a real
        // contest, not a coin flip most rounds ignore or a near-guaranteed
        // disruption every time. chaseTackleUndershoot reuses the exact
        // magnitude (5) already tuned and verified for the "falls short"
        // scene earlier tonight, not a newly-guessed value.
        public float chaseCatchChance = 0.5f;
        public float chaseTackleUndershoot = 5f;
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
        // effectiveDistance defaults to kickDistance (the old behaviour)
        // only so this signature isn't a silent breaking change for any
        // future caller that doesn't know about shortKickUndershoot yet —
        // the one real call site (TapBallAway) always passes its own
        // effectiveKickDistance explicitly, never relies on this default.
        System.Collections.IEnumerator KickAway(Transform t, float zDir, Transform forward, Transform defender, bool isSpeccy, bool humanControlled, int chainDepth = 0, float? effectiveDistance = null)
        {
            float kickDist = effectiveDistance ?? kickDistance;
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
            // here too — this kick's own full kickDist (not halved) can
            // independently push the ball's landing spot outside the
            // field on a deep chain hop, one level below where the
            // TapBallAway-level clamps already catch it. Uses kickDist
            // (the passed-in effective distance), NOT the public
            // kickDistance field directly — see this function's own
            // signature comment for why.
            float kickEndZ = Mathf.Clamp(kickStart.z + zDir * kickDist, -(goalZ - 2f), goalZ - 2f);
            Vector3 kickEnd = new Vector3(kickStart.x, kickStart.y, kickEndZ);

            // 2026-08-23, Shaun: "set a scene up... where the kick...
            // falls short of the forward" — first of a planned series of
            // distinct football scenarios ("this is one scene... finish
            // this first part first" before building the next one, the
            // forward gathering a short kick and snapping at goal).
            // A short kick isn't a markable contest at all — there's no
            // ball in the air near the forward to jump for — so this
            // deliberately does NOT reuse the mark/spoil timing window
            // below (jumpFireAt, NormalMarkHop, "Go up for the mark!").
            // That window is built entirely around a ball arriving AT the
            // forward's position at a fixed time; a short kick lands
            // somewhere else on the ground first, so grading a timed tap
            // against it would be grading a contest that isn't actually
            // happening on screen. Ends the round cleanly here (same
            // "Time's up" no-further-action pattern ContinueChainOrEnd
            // already uses at maxChainDepth) rather than chaining into a
            // new contest — the gather+snap-kick mechanic is explicit
            // future scope, not built yet, so this round has nothing
            // further to hand off to.
            bool isShortKick = kickDist < kickDistance - 0.01f;
            if (isShortKick)
            {
                yield return ShortKickLanding(kickStart, kickEnd, isSpeccy, forward, zDir, humanControlled);
                yield break;
            }

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
                    if (defenderSpoiled)
                    {
                        StartCoroutine(SpoilPunch(defender));
                        yield return new WaitForSeconds(spoilContactBeat);
                    }
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
                    _markHoldReleased = true;
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
                // 2026-08-23, Shaun: "when the forward marks they can play
                // on and kick a snap" — real AFL: after a mark, a player
                // can choose to play on immediately (a spontaneous, lower-
                // ceremony snap) instead of the normal set-shot routine.
                // Deliberately checked BEFORE the shotRangeZ range check
                // below, not gated by it — playing on is a real, if risky,
                // choice even from outside normal shooting range (a genuine
                // "have a crack from here anyway" decision), not just an
                // alternate path once already in range. Same "the human
                // decides for themself, AI gets a real randomized attempt
                // too" fairness principle this file already uses for the
                // mark/spoil/tap-at-goal decisions above.
                bool playOn = false;
                bool aiWantsPlayOn = !humanControlled && Random.value < aiPlayOnChance;
                _message = humanControlled ? "Tap to play on!" : (aiWantsPlayOn ? "Thinking about playing on..." : "Sizing up the shot...");
                float aiPlayOnAt = aiWantsPlayOn ? Random.Range(0.15f, playOnWindow) : 0f;
                float pw = 0f;
                while (pw < playOnWindow)
                {
                    if (_roundId != roundAtStart) yield break;
                    pw += Time.deltaTime;
                    if (humanControlled)
                    {
                        if (Day1Input.TapDown) { playOn = true; break; }
                    }
                    else if (aiWantsPlayOn && pw >= aiPlayOnAt)
                    {
                        playOn = true; break;
                    }
                    yield return null;
                }

                if (playOn)
                {
                    yield return PlayOnSnap(forward, zDir, humanControlled);
                    yield break;
                }

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
                AddScore(humanControlled, 1);
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
                // The ball was previously TELEPORTED from the contest height
                // straight to the ground, then glided off at a constant rate -
                // exactly why a spoil read as the ball being "randomly flwon
                // through for a point" rather than punched through it. The
                // height reset itself is still required (see the block above:
                // frozen at 3.7, taller than the 3.2 posts), so this does not
                // remove it - it ANIMATES it, fast, as the punch knocking the
                // ball down and away.
                {
                    float knockSide = Mathf.Sign(defender && defender.position.x != 0f ? defender.position.x : 1f);
                    Vector3 knockFrom = ball.position;
                    Vector3 knockTo = new Vector3(ball.position.x + knockSide * 0.9f, groundY, ball.position.z);
                    float knockEl = 0f;
                    while (knockEl < spoilKnockDuration)
                    {
                        if (_roundId != roundAtStart) yield break;
                        knockEl += Time.deltaTime;
                        float kf = Mathf.Clamp01(knockEl / spoilKnockDuration);
                        // Fast off the fist, decelerating. A punched ball
                        // leaves hard and slows; it does not glide linearly.
                        ball.position = Vector3.Lerp(knockFrom, knockTo, 1f - Mathf.Pow(1f - kf, 3f));
                        yield return null;
                    }
                    ball.position = knockTo;
                }
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
                float side = Mathf.Sign(defender.position.x == 0f ? 1f : defender.position.x);
                Vector3 behindKickStart = ball.position;
                Vector3 behindTarget = new Vector3(side * 1.6f, behindKickStart.y, zDir * goalZ);
                float behindEl = 0f;
                while (behindEl < shotKickDuration)
                {
                    if (_roundId != roundAtStart) yield break;
                    behindEl += Time.deltaTime;
                    float f = Mathf.Clamp01(behindEl / shotKickDuration);
                    float arc = Mathf.Sin(f * Mathf.PI) * shotKickHeight;
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
                _message = "Kicks in from fullback!";   // matched to the other behind path, 2026-08-28
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
                yield return ContinueChainOrEnd(!humanControlled, clearer, chainDepth);
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
            if (UsingCinemachine)
            {
                vcamKick.transform.position = new Vector3(kickCamSide, kickCamHeight, pivotZ);
                vcamKick.transform.LookAt(new Vector3(0, 3f, pivotZ));
                ActivateVcam(vcamKick);
                return;
            }
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
            if (UsingCinemachine)
            {
                vcamKickOut.transform.position = new Vector3(kickCamSide * 1.6f, kickCamHeight * 1.6f, pivotZ);
                vcamKickOut.transform.LookAt(new Vector3(0, 3f, pivotZ));
                ActivateVcam(vcamKickOut);
                return;
            }
            _mainCam.transform.position = new Vector3(kickCamSide * 1.6f, kickCamHeight * 1.6f, pivotZ);
            _mainCam.transform.LookAt(new Vector3(0, 3f, pivotZ));
        }

        void CutCameraToDefault()
        {
            if (!_mainCam) return;
            if (UsingCinemachine) { ActivateVcam(vcamDefault); return; }
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
            if (UsingCinemachine)
            {
                vcamCloseup.transform.position = forward.position + new Vector3(7f, 3f, 0f);
                vcamCloseup.transform.LookAt(forward.position + Vector3.up * 1.2f);
                ActivateVcam(vcamCloseup);
                return;
            }
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
            if (UsingCinemachine)
            {
                vcamCloseup.transform.position = subject.position + new Vector3(side * 7f, 3f, 0f);
                vcamCloseup.transform.LookAt(subject.position + Vector3.up * 1.2f);
                ActivateVcam(vcamCloseup);
                return;
            }
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

        // 2026-08-23 — the play-on decision window, checked right after a
        // confirmed mark (see TapBallAway/KickAway's own markedResult
        // block). Long enough to be a real, comfortable decision (not a
        // twitch-timing window like markPerfectWindow/defenderSpoilWindow,
        // which grade precision — this just grades whether you tapped at
        // all) but short enough that not deciding reads as a real choice
        // ("play it safe") rather than a stall.
        public float playOnWindow = 1.2f;
        public float aiPlayOnChance = 0.3f;

        // How long the forward's run from wherever they are to the ball's
        // actual short-landing spot takes — same time-based-not-speed-based
        // convention every other RunToZ call in this file already uses
        // (e.g. shotRunInDuration), not derived from the real distance.
        public float gatherRunDuration = 0.8f;

        // 2026-08-23 — the short-kick scene KickAway branches into above
        // (isShortKick). Deliberately its own simple coroutine rather than
        // extra branching bolted into the existing mark/spoil loop: that
        // loop's whole shape (jumpFireAt, markDeadline, defenderSpoilT) is
        // built around timing a catch against a ball that arrives AT a
        // fixed point at a fixed instant, which a short kick genuinely
        // doesn't do. Flies the ball its own real (shorter) distance —
        // no artificial freeze needed here, unlike KickAway's own
        // ball-freeze-at-peak hack for the mark case, since a short kick
        // is supposed to visibly travel less, not travel the same amount
        // and stop early.
        //
        // 2026-08-23, Shaun: "the forward runs up picks up ball and snaps
        // a goal" — second scene in this series, now wired on: once the
        // ball settles, the forward runs onto it (gather) and the round
        // hands off into a shot at goal. Deliberately calls TakeShotAtGoal
        // directly rather than re-implementing a "snap" kick from scratch —
        // that's the file's own proven, already-camera-correct set-shot
        // mechanic; duplicating its drop/power-bar/kick-arc logic here
        // would be exactly the kind of second copy of one fact that this
        // file's own header warns drifts out of sync. The one real
        // difference from a normal mark-then-shot: no step back is skipped
        // here either, actually — TakeShotAtGoal always does its own
        // step-back/run-in "lines up for goal" beat regardless of caller,
        // which reads slightly more deliberate than a real snap, but reuses
        // 100% proven, working code instead of a new near-duplicate with
        // its own risk of drifting. Revisit if this specifically needs to
        // feel snappier once actually played.
        System.Collections.IEnumerator ShortKickLanding(Vector3 kickStart, Vector3 kickEnd, bool isSpeccy, Transform forward, float zDir, bool humanControlled)
        {
            int roundAtStart = _roundId;
            _message = "It falls short!";
            float el = 0f;
            while (el < kickDuration)
            {
                if (_roundId != roundAtStart) yield break;
                el += Time.deltaTime;
                float f = Mathf.Clamp01(el / kickDuration);
                float arc = Mathf.Sin(f * Mathf.PI) * (isSpeccy ? kickHeight : kickHeightNormal);
                ball.position = Vector3.Lerp(kickStart, kickEnd, f) + Vector3.up * arc;
                yield return null;
            }
            ball.position = new Vector3(kickEnd.x, groundY, kickEnd.z);
            // Wide shot pivoted on the ball's ACTUAL landing spot
            // (contestZ), not the default forward-zone pivot — see
            // CutCameraForKick's own contestZ parameter and TakeShotAtGoal's
            // matching one below for why: a short kick can land meaningfully
            // closer to centre than the default pivot assumes, and this is
            // exactly the "camera aimed short of the actual action" bug
            // class already found and fixed once for chained mark contests.
            CutCameraForKick(zDir, kickEnd.z);
            _message = "Loose ball!";
            yield return new WaitForSeconds(catchPause);
            if (_roundId != roundAtStart) yield break;

            // Gather — forward runs onto the loose ball from wherever the
            // short kick's own run/jump left them.
            yield return RunToZ(forward, kickEnd.z, gatherRunDuration);
            if (_roundId != roundAtStart) yield break;
            var gatherHand = FindDeepChild(forward, "RightHand");
            if (gatherHand) ball.position = gatherHand.position;
            _message = "Gathers it!";
            yield return new WaitForSeconds(catchPause);
            if (_roundId != roundAtStart) yield break;

            // 2026-08-28, Shaun: "when the ball falls short the player can
            // gather the ball and kick the snap", then "the snap one will need
            // to be having to just tap and kick i reckon".
            //
            // This used to hand off to TakeShotAtGoal — the full set-shot
            // ritual, a 6m step-back over a second before the power bar even
            // appears. That is football-wrong as well as slow: a set shot is
            // what you get for a MARK. A player who gathers a loose ball has
            // not marked it, so there is no set shot to take — they play on
            // and snap.
            //
            // PlayOnSnap is the same single-tap power bar with the ceremony
            // removed, and it pivots its camera on the kicker's own current
            // position, which after the gather run IS where they gathered it —
            // so the contestZ argument this call used to need is now implicit.
            yield return PlayOnSnap(forward, zDir, humanControlled);
        }

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

        // 2026-08-28, Shaun: "the snap kick probaly needs to be a little more
        // instant like the grab it tap staright away to snap". A set shot's
        // pause is ceremony and belongs there; on a snap it is just lag
        // between gathering the ball and being allowed to kick it.
        public float snapSetupPause = 0.12f;
        public float shotRunInDuration = 0.9f;
        public float shotDropDuration = 0.3f;
        public float shotKickHeight = 3f;
        public float shotKickDuration = 0.9f;
        // How far off-centre (world X) a mistimed kick drifts. Named
        // constants for the actual post positions — must match
        // MainBuildScript's BuildGoalPosts (-1.3/-0.6/0.6/1.3), same
        // "geometry defined once, referenced by name" convention goalZ
        // already uses in this file (see its own comment above). Real AFL
        // scoring, added 2026-08-23 (Shaun: "6 point for goal one for a
        // point"): a miss landing between the inner (goal) and outer
        // (behind) posts is a real, legitimate 1-point outcome, not just
        // "off target" for nothing — shotMissSpread's own range (floor
        // 0.4*2.4=0.96, already inside goalPostOuterX=1.3) already put a
        // near-miss geometrically inside the behind posts before this
        // fix; scoring simply never checked for it.
        public float goalPostInnerX = 0.6f;
        public float goalPostOuterX = 1.3f;
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

        // 2026-08-28, Shaun: "the snaps need to be a quick tap snap not like a
        // set shot", "same as a play on snap". Cutting the setup pause was not
        // enough - the ceremony that actually made it feel like a set shot was
        // this 2.5s power bar, which the snap was still using. A snap is taken
        // under pressure, in about a second.
        public float snapPowerRiseDuration = 0.9f;
        // 2026-08-28, Shaun: "maybe also 10 percent easier for the kid."
        // Band widened 10% about its own centre (0.735) rather than by moving
        // one edge, which would shift WHERE you have to tap as well as how
        // hard it is.
        public float shotPowerGreenMin = 0.608f;
        public float shotPowerGreenMax = 0.862f;
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

        // contestZ: same purpose as CutCameraForKick's own parameter of the
        // same name (see that function) — this shot's default camera pivot
        // (zDir * (goalZ - 5)) is tuned for a kicker standing at the
        // NORMAL full-distance mark spot. 2026-08-23's snap-kick scene
        // (gather a short kick, then shoot from wherever it was gathered)
        // can leave the kicker meaningfully closer to centre than that —
        // the exact "camera aimed short of the actual action" class of bug
        // already found and fixed once for chained mark contests (see this
        // file's own 2026-08-21 session notes). Existing caller (the
        // chained-mark shot) passes nothing, so it's unaffected — it was
        // already correct at the default pivot.
        System.Collections.IEnumerator TakeShotAtGoal(Transform kicker, float zDir, bool humanControlled, float? contestZ = null)
        {
            if (!kicker || !ball) yield break;
            int roundAtStart = _roundId;
            _message = "Lines up for goal...";
            yield return new WaitForSeconds(shotStartPause);
            if (_roundId != roundAtStart) yield break;

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
            CutCameraForKick(zDir, contestZ);
            float markSpotZ = kicker.position.z;
            yield return RunToZ(kicker, markSpotZ - zDir * shotStepBackDistance, shotStepBackDuration);
            if (_roundId != roundAtStart) yield break;
            yield return new WaitForSeconds(shotSetupPause);
            if (_roundId != roundAtStart) yield break;
            yield return RunToZ(kicker, markSpotZ, shotRunInDuration);
            if (_roundId != roundAtStart) yield break;

            yield return ShootAtGoalCore(kicker, zDir, humanControlled);
        }

        // The actual drop/power-bar/kick-arc/scoring — extracted
        // 2026-08-23 so the full set shot (TakeShotAtGoal, after its own
        // step-back/run-in above) and the immediate play-on snap
        // (PlayOnSnap, below — no step-back/run-in at all, that's the
        // whole point of playing on) share one real implementation instead
        // of two copies of the same mechanic that could drift apart, this
        // file's own recurring "one fact in two places" trap.
        System.Collections.IEnumerator ShootAtGoalCore(Transform kicker, float zDir, bool humanControlled, bool isSnap = false)
        {
            int roundAtStart = _roundId;
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
            float riseDuration = isSnap ? snapPowerRiseDuration : shotPowerRiseDuration;
            while (riseEl < riseDuration)
            {
                if (_roundId != roundAtStart) { _shotBarVisible = false; yield break; }
                riseEl += Time.deltaTime;
                _shotBarValue = Mathf.Clamp01(riseEl / riseDuration);
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

            Vector3 kickStart = ball.position;
            Vector3 goalCentre = new Vector3(0, kickStart.y, zDir * goalZ);
            // Real AFL scoring, 2026-08-23 (Shaun: "6 point for goal one
            // for a point"): a miss that still lands between the inner
            // (goal) and outer (behind) posts scores 1, same as the
            // existing rushed-behind case elsewhere in this file — only a
            // miss wide of the OUTER posts scores nothing. Computed after
            // goalCentre.x so isBehind checks where the kick is actually
            // headed, not a second independently-guessed distance.
            bool isBehind = false;
            if (!isGoal)
            {
                float distFromGreen = tapValue < shotPowerGreenMin ? shotPowerGreenMin - tapValue : tapValue - shotPowerGreenMax;
                float side = tapValue < shotPowerGreenMin ? -1f : 1f;
                goalCentre.x = side * shotMissSpread * Mathf.Clamp01(0.4f + distFromGreen * 2f);
                isBehind = Mathf.Abs(goalCentre.x) <= goalPostOuterX;
            }
            _message = isGoal ? "GOAL!" : (isBehind ? "Behind — 1 point." : "Off target!");
            if (isGoal) AddScore(humanControlled, 6);
            else if (isBehind) AddScore(humanControlled, 1);

            // Real then, not fake — the outcome (and therefore the exact
            // target) is already decided before the ball leaves the
            // foot, so the arc just flies straight at it. No freeze-then-
            // tween needed here the way the mark's ball needed one.
            CutCameraBehindGoals(zDir);
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

            // 2026-08-28, Shaun: "it goes straight back to the cntre after a
            // behind on this one", then "after point the kick in not always
            // working". Both describe the same real gap, and "not always" is
            // the clue that identifies it: there are TWO behind paths and only
            // one of them ever kicked in. A rushed behind runs a full kick-out
            // inline; a behind from an actual shot at goal just held the
            // result and fell through to BeginThrow - the centre bounce.
            // 2026-08-28, Shaun: "its inconsistent after points when there is
            // a kick in at this stage". Found by reading a live message trace:
            // there are THREE shot outcomes, not two, and only one of the two
            // misses restarted correctly.
            //
            //   Behind - 1 point.  ->  kick in   ->  centre bounce
            //   Off target!        ->  nothing   ->  centre bounce
            //
            // To a child those are the same event - the shot missed - so
            // restarting them differently reads as the kick-in being broken
            // and firing at random. In football the ball has crossed the goal
            // line either way, so both are a kick-in. Only an actual goal
            // returns to the centre.
            if (!isGoal) yield return KickInAfterBehind(zDir, humanControlled);
        }

        // Deliberately its OWN routine rather than a shared extraction of the
        // rushed-behind kick-out above. That block is the most heavily tuned
        // code in this file - ball-tracked-to-boot, camera cut timed to the
        // leg snap, and a documented defect where "kicker and ball were two
        // unrelated objects that happened to animate at the same time". It
        // works. Refactoring it to serve a second caller risks the beat that
        // is already right in order to fix the one that never existed, so this
        // reuses the same helpers (RunToZ / CutCameraForKickOut / KickMotion)
        // without touching it.
        System.Collections.IEnumerator KickInAfterBehind(float zDir, bool crocsInPossession)
        {
            // The team that was SCORED ON kicks in - same rule as the
            // rushed-behind path's own defender pick.
            Transform defender = crocsInPossession ? rooDefender : crocDefender;
            if (!defender || !ball) yield break;
            int roundAtStart = _roundId;

            _message = "Kick in...";
            Vector3 goalSquare = new Vector3(0f, defender.position.y, zDir * goalZ);
            defender.position = goalSquare;
            defender.rotation = Quaternion.Euler(0f, zDir > 0f ? 180f : 0f, 0f);

            float targetZ = zDir * goalZ - zDir * kickOutDistance;
            CutCameraForKickOut(zDir * goalZ, targetZ);

            // Ball onto the boot and kept there through the motion, so the
            // kick reads as caused by the player rather than the ball simply
            // moving on its own - the exact defect the kick-out beat had.
            var boot = FindDeepChild(defender, "RightFoot");
            if (ball) ball.position = boot ? boot.position : defender.position + Vector3.up * 0.5f;
            yield return new WaitForSeconds(kickOutPause * 0.5f);
            if (_roundId != roundAtStart) yield break;

            StartCoroutine(KickMotion(defender, shotKickDuration * 0.6f));
            float settle = 0f;
            while (settle < shotKickDuration * 0.25f)
            {
                if (_roundId != roundAtStart) yield break;
                if (ball && boot) ball.position = boot.position;
                settle += Time.deltaTime;
                yield return null;
            }

            _message = "Kicks in from fullback!";
            Vector3 from = ball.position;
            Vector3 to = new Vector3(0f, from.y, targetZ);
            float el2 = 0f;
            while (el2 < shotKickDuration)
            {
                if (_roundId != roundAtStart) yield break;
                el2 += Time.deltaTime;
                float f = Mathf.Clamp01(el2 / shotKickDuration);
                float arc = Mathf.Sin(f * Mathf.PI) * shotKickHeight;
                ball.position = Vector3.Lerp(from, to, f) + Vector3.up * arc;
                yield return null;
            }
            ball.position = to;
            yield return new WaitForSeconds(shotResultHold * 0.5f);
        }

        // 2026-08-23, Shaun: "when the forward marks they can play on and
        // kick a snap." No step-back/run-in ritual at all — that deliberate
        // ceremony IS what a normal set shot has and a snap doesn't, real
        // football. Still cuts to the correct wide kick camera first
        // (pivoted on the kicker's own real current position via contestZ,
        // same "aim it at where the action actually is" fix TakeShotAtGoal
        // itself needed — see that function's own contestZ comment) and
        // still gives a real beat before the power bar starts so the cut
        // doesn't feel instant/jarring, just a much shorter one
        // (shotSetupPause, not the full step-back+pause+run-in sequence).
        System.Collections.IEnumerator PlayOnSnap(Transform kicker, float zDir, bool humanControlled)
        {
            if (!kicker || !ball) yield break;
            int roundAtStart = _roundId;
            _message = "Plays on — snaps for goal!";
            CutCameraForKick(zDir, kicker.position.z);
            yield return new WaitForSeconds(snapSetupPause);
            if (_roundId != roundAtStart) yield break;
            yield return ShootAtGoalCore(kicker, zDir, humanControlled, isSnap: true);
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
        // carriesBall: 2026-08-23, added for the chasing rover (see the
        // catch-causes-a-short-kick mechanic in TapBallAway) — this
        // function unconditionally moved the single global `ball` to the
        // runner's own hand every frame, correct for the ball-carrying
        // rover but would hijack the ball onto the CHASER's hand instead
        // if reused as-is. Existing callers all pass nothing, so they keep
        // carrying the ball exactly as before.
        System.Collections.IEnumerator RunStraight(Transform t, float zDir, bool carriesBall = true)
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
                if (carriesBall && ball)
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
            if (carriesBall && ball)
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
            float heightScale = reachesBall ? RuckLeapScale : 0.5f;
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
        // 2026-08-28, Shaun: "with the rushed behind the ball just flys
        // through cannot tell its actually been spoiled", and "the chasing
        // abilty only working sometimes".
        //
        // Both had the same root cause, and it is worth naming because it is a
        // whole class of bug in this file rather than two incidents: the
        // OUTCOME was decided in logic and narrated in text, but never
        // animated. defenderSpoiled swapped a message. caughtByChaser swapped
        // a message and a kick distance. Neither put anything on screen, so
        // from the couch nothing happened - which is indistinguishable from
        // the feature being broken. That is exactly how the chase came to be
        // reported as "only working sometimes" when it was firing correctly.
        //
        // Same technique as KickMotion below: direct bone rotation with the
        // Animator switched off for the duration, no clip needed.
        //
        // Deliberately ARM BONES ONLY, no position or root rotation. These
        // characters are already being moved by RunStraight when these fire,
        // and a motion that fought it for the transform would read worse than
        // no motion at all - the same "two unrelated objects animating at the
        // same time" failure the kick-out beat already had once.
        public float spoilPunchDuration = 0.42f;
        public float spoilPunchAngle = 95f;   // forearm snap, not a whole-arm swing
        System.Collections.IEnumerator SpoilPunch(Transform t)
        {
            if (!t) yield break;
            // 2026-08-28 - REWRITTEN. The first version drove RightArm and
            // toggled the Animator, which was a real bug: NormalMarkHop is
            // ALREADY running on this same defender (started at the mark
            // contest), already has the Animator disabled, and already poses
            // BOTH arms to +-155 and holds them there. So this fought it for
            // the same bone every frame and then, on finishing, restored the
            // arm and re-enabled the Animator mid-jump - undoing the pose it
            // had been fighting. That is why the spoil never read.
            //
            // Reference for the correct pose: a real photo Shaun sent of a
            // local match, one player at full stretch above the pack. What
            // separates a SPOIL from a mark attempt in that photo is not both
            // arms up - it is ONE arm punching through past full extension
            // while the other stays tucked. NormalMarkHop already provides the
            // leap and the raised arm; this adds only the punch on top.
            //
            // Drives RightForeArm, which NormalMarkHop does not touch, so the
            // two compose instead of competing. Deliberately does NOT touch
            // the Animator - NormalMarkHop owns that for the duration of the
            // jump, and re-enabling it here is precisely what broke the pose.
            var foreArm = FindDeepChild(t, "RightForeArm");
            if (!foreArm) yield break;
            Quaternion start = foreArm.localRotation;

            float el = 0f;
            while (el < spoilPunchDuration)
            {
                el += Time.deltaTime;
                float f = Mathf.Clamp01(el / spoilPunchDuration);
                // A spoil is a strike: snap out fast over the first third,
                // settle back slower. Same asymmetry KickMotion uses for the
                // boot, and for the same reason.
                float angle = f < 0.3f
                    ? Mathf.Lerp(0f, -spoilPunchAngle, Mathf.Sin((f / 0.3f) * Mathf.PI * 0.5f))
                    : Mathf.Lerp(-spoilPunchAngle, 0f, (f - 0.3f) / 0.7f);
                foreArm.localRotation = start * Quaternion.Euler(0f, 0f, angle);
                yield return null;
            }
            foreArm.localRotation = start;
        }

        public float tackleDuration = 0.55f;

        // 2026-08-28, Shaun: "remember lots of pauses and also one thing at
        // atime". This is a sensory-friendly app for neurodivergent kids, so
        // beats have to land one at a time and be given room to register -
        // a contact that resolves and is immediately overwritten by the next
        // message reads as noise rather than as a moment.
        //
        // The motions themselves are already sequential: the tackle is a
        // blocking yield, and the spoil punch is fired alongside the
        // defender's existing jump because a spoil IS a jump and a punch, one
        // action rather than two. What was missing was the beat AFTER, so the
        // camera holds on the result before play moves on.
        public float contactHoldPause = 0.7f;

        // 2026-08-28, Shaun: "the rushed point still is being randomly flwon
        // through for a point". A full contactHoldPause on the spoil was
        // actively wrong - it put 0.7s between the fist and the ball moving,
        // so the two read as unrelated events rather than cause and effect.
        // This is just long enough to see contact; the ball leaves on the
        // punch. The knock itself is what makes it look struck.
        public float spoilContactBeat = 0.22f;
        public float spoilKnockDuration = 0.26f;
        System.Collections.IEnumerator TackleGrab(Transform chaser, Transform carrier)
        {
            var cl = chaser ? FindDeepChild(chaser, "LeftArm") : null;
            var cr = chaser ? FindDeepChild(chaser, "RightArm") : null;
            var bl = carrier ? FindDeepChild(carrier, "LeftArm") : null;
            var br = carrier ? FindDeepChild(carrier, "RightArm") : null;
            if (!cl && !cr && !bl && !br) yield break;

            Quaternion clS = cl ? cl.localRotation : Quaternion.identity;
            Quaternion crS = cr ? cr.localRotation : Quaternion.identity;
            Quaternion blS = bl ? bl.localRotation : Quaternion.identity;
            Quaternion brS = br ? br.localRotation : Quaternion.identity;

            // Cut in close on the tackle. Without this the motion plays but
            // nobody sees it: verified in a live capture where the carrier's
            // arms clearly flew up on "Caught by the Croc!" while the croc
            // doing the tackling was already outside the frame. An animation
            // the camera is not pointing at is the same as no animation, which
            // is the very failure this whole change exists to fix.
            if (carrier)
            {
                float tside = Mathf.Sign(carrier.position.x == 0f ? 1f : carrier.position.x);
                CutCameraToMarkCloseup(carrier, tside);
            }

            var ca = chaser ? chaser.GetComponentInChildren<Animator>() : null;
            var ba = carrier ? carrier.GetComponentInChildren<Animator>() : null;
            bool caOn = ca && ca.enabled, baOn = ba && ba.enabled;
            if (ca) ca.enabled = false;
            if (ba) ba.enabled = false;

            float el = 0f;
            while (el < tackleDuration)
            {
                el += Time.deltaTime;
                float f = Mathf.Clamp01(el / tackleDuration);
                float grab = Mathf.Sin(f * Mathf.PI);          // out and back
                // Chaser reaches with both arms; carrier's fly up as they are
                // caught, a touch wider so the two read as cause and effect
                // rather than as the same gesture played twice.
                if (cl) cl.localRotation = clS * Quaternion.Euler(0f, 0f, grab * 95f);
                if (cr) cr.localRotation = crS * Quaternion.Euler(0f, 0f, -grab * 95f);
                if (bl) bl.localRotation = blS * Quaternion.Euler(0f, 0f, grab * 130f);
                if (br) br.localRotation = brS * Quaternion.Euler(0f, 0f, -grab * 130f);
                yield return null;
            }
            if (cl) cl.localRotation = clS;
            if (cr) cr.localRotation = crS;
            if (bl) bl.localRotation = blS;
            if (br) br.localRotation = brS;
            if (ca && caOn) ca.enabled = true;
            if (ba && baOn) ba.enabled = true;
            // Hold on it before the next beat starts.
            yield return new WaitForSeconds(contactHoldPause);
        }

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
            const float heightScale = 1.65f;
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
            if (_scoreStyle == null)
            {
                _scoreStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.RoundToInt(Screen.height * 0.045f),
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.white }
                };
            }
            if (_roundStyle == null)
            {
                _roundStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.RoundToInt(Screen.height * 0.028f),
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(1f, 0.85f, 0.3f) }
                };
            }
            // Small persistent round-name strip (Wildcard Round / Finals /
            // Grand Final) above the score itself — the finals series is
            // otherwise invisible outside the transition messages.
            int roundH = Mathf.RoundToInt(Screen.height * 0.04f);
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, roundH), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(0, 0, Screen.width, roundH), _fixtures[_fixtureIndex].round.ToUpper(), _roundStyle);

            // Persistent scoreboard, always visible — separate strip above
            // the transient message bar below so a score change doesn't
            // fight for the same space as "GOAL!"/"Centre bounce..." etc.
            int scoreH = Mathf.RoundToInt(Screen.height * 0.065f);
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(new Rect(0, roundH, Screen.width, scoreH), Texture2D.whiteTexture);
            GUI.color = Color.white;
            float clockSecs = Mathf.Max(0f, _quarterTimeRemaining);
            string clock = _matchOver ? "FULL TIME" : "Q" + _quarter + "  " + Mathf.FloorToInt(clockSecs / 60f) + ":" + Mathf.FloorToInt(clockSecs % 60f).ToString("00");
            GUI.Label(new Rect(0, roundH, Screen.width, scoreH), HomeTeamShort + " " + _crocScore + "   " + clock + "   " + _rooScore + " " + _fixtures[_fixtureIndex].awayShort, _scoreStyle);

            int panelH = Mathf.RoundToInt(Screen.height * 0.14f);
            int y = Mathf.RoundToInt(Screen.height * 0.08f) + scoreH + roundH;
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
    }
}
