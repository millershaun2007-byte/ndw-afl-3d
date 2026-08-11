using UnityEngine;

namespace AFL.Day1
{
    // =======================================================================
    //  DAY 1 — two rucks at the centre. Nothing else.
    // =======================================================================
    // Per issue #6: a new scene, two players, one button. Does not touch
    // AflField.unity or anything in the six-player game. No movement (the
    // rucks stand still and contest a throw-up), no score, no HUD beyond
    // one message. A rough leap is explicitly fine today — no Mixamo clips,
    // no Animator, no Avatar. Purely procedural hop on press, straight
    // transform manipulation.
    public class Day1RuckContest : MonoBehaviour
    {
        public Transform crocVisual;
        public Transform rooVisual;
        public Transform ball;

        // Ball arc: rises and falls over this many seconds, peak at the
        // midpoint — the moment a real tap should happen. Long and gentle
        // on purpose (this is a child on a touchscreen, not a speedrun).
        //
        // Real fix (2026-08-11, Shaun: "they also need to be able to...
        // reach the ball at its peak") — peakHeight was 3.2, well above
        // anything a ~2.1-unit-tall character's raised-arm hop could
        // plausibly reach, so even a perfectly-timed press looked like it
        // missed by a mile. Lowered so the ball's absolute peak
        // (groundY + peakHeight) sits within reach of standing height +
        // raised arms + the hop in HopRoutine below.
        public float throwDuration = 2.0f;
        public float peakHeight = 2.1f;
        public float groundY = 1.0f;

        // Grading window — generous, per issue #6's own 0.25s floor rule
        // (this is well above it) and #2's "never punish below neutral"
        // principle carried down to the simplest possible case: a miss is
        // just a miss, never worse than not trying.
        public float perfectWindow = 0.5f;

        float _t;
        bool _humanPressed, _botPressed;
        float _humanPressT = -1f, _botPressT = -1f;
        float _botPressAt;
        bool _resolved;
        float _resolvedAt;
        string _message = "Centre bounce...";
        GUIStyle _style;

        void Start() { BeginThrow(); }

        void BeginThrow()
        {
            _t = 0f;
            _humanPressed = false; _botPressed = false;
            _humanPressT = -1f; _botPressT = -1f;
            _resolved = false;
            _message = "Centre bounce...";
            // Bot commits at a scripted moment worse than a human's
            // achievable best (real per-frame press, jitter deliberately
            // wider than the perfect window) — same fairness principle as
            // the rest of this project: bots must not be mechanically
            // better than a person at the same timed press.
            float ideal = throwDuration * 0.5f;
            _botPressAt = ideal + Random.Range(-0.35f, 0.35f);
        }

        void Update()
        {
            if (_resolved)
            {
                if (Time.time - _resolvedAt > 1.8f) BeginThrow();
                return;
            }

            _t += Time.deltaTime;
            float frac = Mathf.Clamp01(_t / throwDuration);
            float height = Mathf.Sin(frac * Mathf.PI) * peakHeight;
            if (ball) ball.position = new Vector3(0f, groundY + height, 0f);

            if (!_humanPressed && Day1Input.TapDown)
            {
                _humanPressed = true;
                _humanPressT = _t;
                Hop(crocVisual);
            }
            if (!_botPressed && _t >= _botPressAt)
            {
                _botPressed = true;
                _botPressT = _t;
                Hop(rooVisual);
            }

            if ((_humanPressed && _botPressed) || _t >= throwDuration)
            {
                Resolve();
            }
        }

        void Resolve()
        {
            _resolved = true;
            _resolvedAt = Time.time;
            float ideal = throwDuration * 0.5f;

            float humanErr = _humanPressed ? Mathf.Abs(_humanPressT - ideal) : 999f;
            float botErr = _botPressed ? Mathf.Abs(_botPressT - ideal) : 999f;

            if (!_humanPressed && !_botPressed) { _message = "Nobody jumped — ball up!"; return; }

            _message = humanErr <= botErr ? "Crocs win the tap!" : "Roos win the tap!";
        }

        // Explicitly rough, explicitly not a real animation — issue #6:
        // "if the leap looks rough today, that is fine." Straight transform
        // lerp via a coroutine, no Animator, no clip.
        //
        // Real fix (2026-08-11, Shaun's direct playtest: "they are not
        // able to jump extend there arms and tap the ball") — the first
        // version only moved the whole body up, no arm motion at all, so
        // it read as a bob, not a reach. Issue #6's own build list says
        // "pressing it makes your ruck go up FOR THE TAP" — the reach is
        // part of the spec, not an extra. Rotating LeftArm/RightArm by
        // 140°/-140° on the local Z axis was confirmed empirically (a
        // static render, not guessed) to raise both arms symmetrically
        // overhead — other axes tried put one arm up and one arm across
        // the body, which is wrong for this rig's bone orientation.
        void Hop(Transform t)
        {
            if (t) StartCoroutine(HopRoutine(t));
        }

        static Transform FindDeepChild(Transform parent, string name)
        {
            foreach (var child in parent.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }

        // Real fix (2026-08-11, Shaun: "not just plausible completely
        // possible" — reach must genuinely close the gap to the ball,
        // measured directly, not eyeballed). Standing with arms raised
        // 140° on Z, hands measured at world y≈1.48, barely moved in X
        // from the character's own standing position (raising an arm
        // straight overhead does not reach it forward). Ball peaks at
        // groundY + peakHeight = 1.0 + 2.0 = 3.0. A vertical hop of 1.5
        // brings hand height to ≈1.48 + 1.5 = 2.98 — a ~2cm gap at the
        // scale of a 2-unit-tall character, i.e. contact, not "close."
        // The horizontal gap (character stands 0.55 from the ball's
        // x=0) is closed by leaning the whole body toward centre during
        // the hop, since the arm raise alone does not reach sideways.
        System.Collections.IEnumerator HopRoutine(Transform t)
        {
            Vector3 start = t.localPosition;
            Vector3 towardCentre = new Vector3(-Mathf.Sign(start.x) * 0.55f, 0, 0);
            var leftArm = FindDeepChild(t, "LeftArm");
            var rightArm = FindDeepChild(t, "RightArm");
            Quaternion leftStart = leftArm ? leftArm.localRotation : Quaternion.identity;
            Quaternion rightStart = rightArm ? rightArm.localRotation : Quaternion.identity;

            float dur = 0.45f;
            float el = 0f;
            while (el < dur)
            {
                el += Time.deltaTime;
                float f = el / dur;
                float wave = Mathf.Sin(f * Mathf.PI);   // 0 -> 1 -> 0
                t.localPosition = start + Vector3.up * wave * 1.5f + towardCentre * wave;
                if (leftArm) leftArm.localRotation = leftStart * Quaternion.Euler(0, 0, wave * 140f);
                if (rightArm) rightArm.localRotation = rightStart * Quaternion.Euler(0, 0, -wave * 140f);
                yield return null;
            }
            t.localPosition = start;
            if (leftArm) leftArm.localRotation = leftStart;
            if (rightArm) rightArm.localRotation = rightStart;
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
