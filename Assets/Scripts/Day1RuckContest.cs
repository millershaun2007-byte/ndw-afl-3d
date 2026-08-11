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
        float _resolvedAt;
        string _message = "Centre bounce...";
        GUIStyle _style;

        void Start() { BeginThrow(); }

        void BeginThrow()
        {
            _t = 0f;
            _humanPressed = false;
            _bestHumanErr = float.MaxValue;
            _ballFrozen = false;
            _hopFired = false;
            _resolved = false;
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
                if (Time.time - _resolvedAt > 2.2f) BeginThrow();
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
            // Receive height around chest level, not ground — reads as a
            // catch rather than the ball rolling to their feet.
            Vector3 end = rover ? rover.position + Vector3.up * 1.3f : start + Vector3.down * 0.6f;
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
