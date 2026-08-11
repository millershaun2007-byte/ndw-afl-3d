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
        public Transform ball;

        public float throwDuration = 2.6f;
        public float peakHeight = 2.1f;
        public float groundY = 1.0f;
        public float hopDuration = 0.45f;
        public float perfectWindow = 0.5f;

        float _t;
        bool _humanPressed;
        float _humanPressT = -1f;
        float _botPressT;
        float _hopFireAt;
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
            _humanPressT = -1f;
            _hopFired = false;
            _resolved = false;
            _message = "Centre bounce...";
            float ideal = throwDuration * 0.5f;
            _botPressT = ideal + Random.Range(-0.35f, 0.35f);
            _hopFireAt = ideal - hopDuration / 2f;
        }

        void Update()
        {
            if (_resolved)
            {
                if (Time.time - _resolvedAt > 2.2f) BeginThrow();
                return;
            }

            _t += Time.deltaTime;

            // Ball follows the free arc only up until contact — after that
            // the tap-away coroutine below owns its position.
            if (!_hopFired)
            {
                float frac = Mathf.Clamp01(_t / throwDuration);
                float height = Mathf.Sin(frac * Mathf.PI) * peakHeight;
                if (ball) ball.position = new Vector3(0f, groundY + height, 0f);
            }

            if (!_humanPressed && Day1Input.TapDown)
            {
                _humanPressed = true;
                _humanPressT = _t;
            }

            if (!_hopFired && _t >= _hopFireAt)
            {
                _hopFired = true;
                ResolveAndContest();
            }

            // A late press after the contest already resolved gets nothing
            // extra — the beat has passed, matching "no press = no jump"
            // rather than a confusing second animation on top of the result.
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
        void ResolveAndContest()
        {
            float ideal = throwDuration * 0.5f;
            float humanErr = _humanPressed ? Mathf.Abs(_humanPressT - ideal) : 999f;
            float botErr = Mathf.Abs(_botPressT - ideal);
            // Real fix (2026-08-11, Shaun playtest: "human does not have a
            // chance"). perfectWindow was declared but never actually used —
            // the human had to out-precise a bot whose own error is capped
            // at +/-0.35s, with zero visual cue for exactly when "ideal" is.
            // That's a fair fight for the bot and an unwinnable one for a
            // human going on the ball's height alone. A press anywhere
            // inside the generous perfectWindow now just wins outright;
            // only a press that misses that window falls back to the raw
            // comparison (a real but rare comeback case).
            bool crocWins = _humanPressed && (humanErr <= perfectWindow || humanErr <= botErr);

            _message = _humanPressed
                ? (crocWins ? "Crocs win the tap!" : "Roos win the tap!")
                : "Too slow — Roos win the tap!";

            Hop(crocVisual, crocWins);
            Hop(rooVisual, !crocWins);
            StartCoroutine(TapBallAway(crocWins));

            _resolved = true;
            _resolvedAt = Time.time;
        }

        // Ball leaves the centre toward the winning side's attacking end
        // and away from where it was contested — a real "tapped away",
        // not just a touch. Home (Croc) attacks +Z, Away (Roo) attacks -Z
        // in this project's existing convention.
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
            // Real fix (2026-08-11, Shaun playtest: "ball is being tapped
            // the wrong way"). Croc starts at x=-0.55 and hops TOWARD centre
            // (+x) to reach the ball; Roo starts at x=+0.55 and hops toward
            // centre (-x). This used to send the ball in the opposite x
            // direction from whichever character just reached for it — the
            // ball flew back over the winner's own hop instead of continuing
            // the direction their tap was already moving in. Now it follows
            // through in the same direction as the winner's reach.
            Vector3 dir = new Vector3(crocWins ? 0.6f : -0.6f, -0.3f, crocWins ? 1f : -1f).normalized;
            Vector3 end = start + dir * 3.2f + Vector3.down * 0.6f;
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
            float heightScale = reachesBall ? 1.5f : 0.5f;
            Vector3 towardCentre = reachesBall
                ? new Vector3(-Mathf.Sign(start.x) * 0.55f, 0, 0)
                : Vector3.zero;
            float armAngle = reachesBall ? 140f : 60f;

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
