using UnityEngine;

namespace AFL
{
    // =======================================================================
    //  BEAT PROMPT — the one verb, reused for every beat (2026-08-11 rewrite)
    // =======================================================================
    // Per the rewrite brief on issue #1: every beat (ruck tap, clearance
    // kick, mark contest, set shot) reduces to the same gesture — watch a
    // value move, tap MARK, get graded on how close you were. This class is
    // the SINGLE SOURCE OF TRUTH for that value: whatever OnGUI draws is
    // exactly what Resolve() grades against, because they both read
    // CurrentValue. There is no second, invisible number anywhere (the bug
    // class section 5 of the brief calls out by name — grading used to read
    // PredictReach() while the player could only ever see a HUD number).
    //
    // Two visual modes:
    //  - Sweep: a value oscillates between -1 and 1 (an arrow swinging, or
    //    an aim angle). "Perfect" is a caller-supplied target (0 for
    //    straight-ahead, or wherever the ball actually is for the ruck tap).
    //  - Ring: a value counts 0 -> 1 once (a ring closing on a falling
    //    ball). "Perfect" is always 1 — the tap should land as the ring
    //    finishes closing.
    // Bots use the identical CurrentValue and IdealValue this draws from —
    // see AFLBotBrain's use of Prompt.CurrentValue — so a bot's "skill" is
    // expressed as reaction delay and press-time jitter around the same
    // real number a human sees, not a separate hidden calculation.
    public class AFLBeatPrompt : MonoBehaviour
    {
        public enum Mode { Sweep, Ring }

        public Mode CurrentMode { get; private set; }
        public bool IsLive { get; private set; }
        public float CurrentValue { get; private set; }   // Sweep: -1..1.  Ring: 0..1.
        public float IdealValue { get; private set; }      // Sweep: caller-set target. Ring: always 1.
        public string PromptText { get; private set; } = "";

        float _sweepDir = 1f;
        public float sweepSpeed = 1.6f;       // units/sec across the -1..1 range
        public float ringDuration = 1.6f;     // seconds for the ring to close 0->1

        // Tolerance bands, shared identically by grading AND by whatever
        // visual feedback wants to show "how close" (e.g. colour the ring).
        public float perfectBand = 0.08f;
        public float goodBand = 0.20f;
        public float lateBand = 0.38f;

        GUIStyle _panelStyle, _textStyle;

        void Update()
        {
            if (!IsLive) return;

            if (CurrentMode == Mode.Sweep)
            {
                CurrentValue += _sweepDir * sweepSpeed * Time.deltaTime;
                if (CurrentValue > 1f) { CurrentValue = 1f; _sweepDir = -1f; }
                if (CurrentValue < -1f) { CurrentValue = -1f; _sweepDir = 1f; }
            }
            else // Ring
            {
                CurrentValue = Mathf.Clamp01(CurrentValue + Time.deltaTime / ringDuration);
            }
        }

        public void BeginSweep(string prompt, float idealValue)
        {
            CurrentMode = Mode.Sweep;
            IsLive = true;
            CurrentValue = 0f;
            _sweepDir = 1f;
            IdealValue = Mathf.Clamp(idealValue, -1f, 1f);
            PromptText = prompt;
        }

        public void BeginRing(string prompt)
        {
            CurrentMode = Mode.Ring;
            IsLive = true;
            CurrentValue = 0f;
            IdealValue = 1f;
            PromptText = prompt;
        }

        public void Stop() { IsLive = false; }

        /// Grades however close CurrentValue currently is to IdealValue,
        /// using the exact same value just drawn on screen. Returns
        /// (grade 0..1, errorMagnitude) — errorMagnitude is in the same
        /// units as the band constants so a caller can log/tune without
        /// re-deriving it.
        public (float grade, float error) Resolve()
        {
            float error = Mathf.Abs(CurrentValue - IdealValue);
            float grade;
            if (error <= perfectBand) grade = Mathf.Lerp(1f, 0.88f, error / perfectBand);
            else if (error <= goodBand) grade = Mathf.Lerp(0.86f, 0.6f, Mathf.InverseLerp(perfectBand, goodBand, error));
            else if (error <= lateBand) grade = Mathf.Lerp(0.58f, 0.25f, Mathf.InverseLerp(goodBand, lateBand, error));
            else grade = 0.08f;
            return (grade, error);
        }

        // ---- Legible HUD (issue #1 section 9): large, high-contrast, a
        // dark panel behind the text, font size derived from Screen.height
        // rather than a fixed pixel count so it still reads on a tablet. ----
        void OnGUI()
        {
            if (!IsLive) return;
            EnsureStyles();

            int panelW = Mathf.RoundToInt(Screen.width * 0.86f);
            int panelH = Mathf.RoundToInt(Screen.height * 0.16f);
            int x = (Screen.width - panelW) / 2;
            int y = Mathf.RoundToInt(Screen.height * 0.68f);

            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(new Rect(x, y, panelW, panelH), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(x, y + panelH * 0.06f, panelW, panelH * 0.4f), PromptText, _textStyle);

            // The moving indicator itself — a simple bar with a marker, not
            // decorative, since its position IS the graded value.
            int barY = y + Mathf.RoundToInt(panelH * 0.62f);
            int barH = Mathf.RoundToInt(panelH * 0.22f);
            int barX = x + Mathf.RoundToInt(panelW * 0.06f);
            int barW = panelW - Mathf.RoundToInt(panelW * 0.12f);

            GUI.color = new Color(1f, 1f, 1f, 0.25f);
            GUI.DrawTexture(new Rect(barX, barY, barW, barH), Texture2D.whiteTexture);

            float t = CurrentMode == Mode.Sweep ? (CurrentValue + 1f) / 2f : CurrentValue;
            float idealT = CurrentMode == Mode.Sweep ? (IdealValue + 1f) / 2f : 1f;

            // Ideal-zone marker so the target itself is visible, not just
            // implied — this is what makes the grading feel fair: the
            // "correct" spot is drawn, not hidden.
            int idealX = barX + Mathf.RoundToInt(barW * Mathf.Clamp01(idealT)) - 3;
            GUI.color = new Color(0.6f, 1f, 0.6f, 0.9f);
            GUI.DrawTexture(new Rect(idealX, barY - 4, 6, barH + 8), Texture2D.whiteTexture);

            int markerX = barX + Mathf.RoundToInt(barW * Mathf.Clamp01(t)) - 5;
            GUI.color = new Color(1f, 0.85f, 0.2f, 1f);
            GUI.DrawTexture(new Rect(markerX, barY - 6, 10, barH + 12), Texture2D.whiteTexture);

            GUI.color = Color.white;
        }

        void EnsureStyles()
        {
            if (_textStyle != null) return;
            // Same fix as AFLGameManager's score HUD (2026-08-16) — scale off
            // the smaller of width/height so this never sizes too wide for a
            // narrow embedded canvas, not just tall ones.
            int fontSize = Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height) * 0.045f);
            _textStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
        }
    }
}
