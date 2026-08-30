using UnityEngine;

namespace AFL.Day1
{
    // =======================================================================
    //  THE MATCH — scoreboard, four quarters, and a finals series (2026-08-31)
    // =======================================================================
    // Shaun: "add the scoreboard, the team names Mount Duneed Cats, a wildcard
    // round, semi finals and a grand final, having 4 3 minute quarters."
    //
    // The scoreboard and quarter clock are PORTED from ndw-footy's
    // FootyMatch.cs, not re-derived — that one is playtested and signed off
    // ("and a scorecard", 2026-08-30), down to the goals.behinds (total) card,
    // the leader's row going green, and the clock turning amber in the last 20
    // seconds. Same numbers, same layout. What is new here is the finals
    // series on top of it.
    //
    // This owns the scoreboard and NOTHING else. It does not move a player,
    // touch the ball or drive a camera — Day1RuckContest still runs every
    // passage exactly as it did, and simply reports what it scored. The one
    // thing this does reach for is holdRestart, to stop a new passage starting
    // during a quarter break or after the siren; the passage already running
    // is always allowed to finish.
    public class AFLMatch : MonoBehaviour
    {
        public Day1RuckContest contest;

        [Header("Teams")]
        // Shaun, 2026-08-30 (ndw-footy): "Mount Duneed Cats are the humans."
        // The human plays the Croc side here — Day1RuckContest passes the same
        // flag as both `humanControlled` and `crocsInPossession`.
        public string homeName = "Mount Duneed Cats";

        // THE FINALS SERIES. Three matches, sudden death, in order. Opponents
        // are local club names rather than AFL club nicknames — deliberate:
        // CLAUDE.md's own trademark rule (the reason Chaos Sports is never
        // called "Olympics") applies just as much to a team called the Tigers.
        [System.Serializable]
        public class Final
        {
            public string title;
            public string opponent;
            public Final(string t, string o) { title = t; opponent = o; }
        }

        public Final[] series =
        {
            new Final("WILDCARD",    "Anakie"),
            new Final("SEMI FINAL",  "Inverleigh"),
            new Final("GRAND FINAL", "Winchelsea"),
        };

        [Header("Match clock")]
        // FOUR THREE-MINUTE QUARTERS, same as ndw-footy's.
        public int quarters = 4;
        public float quarterSeconds = 180f;
        public float quarterBreakSeconds = 5f;
        [Tooltip("How long the result of a final stays up before the next one starts.")]
        public float finalResultSeconds = 5f;

        enum Phase { Play, QuarterBreak, FullTime, SeriesOver }

        int _final;          // index into series
        int _quarter = 1;
        float _clock;
        float _breakT;
        Phase _phase = Phase.Play;
        string _headline = "";
        string _subline = "";

        int _homeGoals, _homeBehinds, _awayGoals, _awayBehinds;

        int HomePoints => _homeGoals * 6 + _homeBehinds;
        int AwayPoints => _awayGoals * 6 + _awayBehinds;
        string AwayName => series != null && _final < series.Length ? series[_final].opponent : "Visitors";
        string Title => series != null && _final < series.Length ? series[_final].title : "MATCH";

        void Start()
        {
            if (!contest) contest = FindAnyObjectByType<Day1RuckContest>();
            StartFinal(0);
        }

        void StartFinal(int index)
        {
            _final = Mathf.Clamp(index, 0, Mathf.Max(0, series.Length - 1));
            _quarter = 1;
            _clock = quarterSeconds;
            _breakT = 0f;
            _homeGoals = _homeBehinds = _awayGoals = _awayBehinds = 0;
            _phase = Phase.Play;
            _headline = Title;
            _subline = homeName + " v " + AwayName;
            if (contest) contest.holdRestart = false;
        }

        /// <summary>Day1RuckContest calls this when a passage produces a
        /// score. 6 for a goal, 1 for a behind; byHome is the Croc side.</summary>
        public void Record(int points, bool byHome)
        {
            // The siren does not wipe a score that was already in the air —
            // but nothing counts once the match itself is decided.
            if (_phase == Phase.SeriesOver) return;
            if (points == 6) { if (byHome) _homeGoals++; else _awayGoals++; }
            else if (points == 1) { if (byHome) _homeBehinds++; else _awayBehinds++; }
        }

        void Update()
        {
            switch (_phase)
            {
                case Phase.Play:
                    _clock -= Time.deltaTime;
                    if (_clock > 0f) return;
                    _clock = 0f;
                    if (_quarter >= quarters) EndOfMatch();
                    else
                    {
                        _phase = Phase.QuarterBreak;
                        _breakT = 0f;
                        _headline = QuarterName(_quarter) + " TIME";
                        _subline = ScoreLine();
                        // Let the passage in progress play out; just don't
                        // start another one until the break is over.
                        if (contest) contest.holdRestart = true;
                    }
                    return;

                case Phase.QuarterBreak:
                    _breakT += Time.deltaTime;
                    if (_breakT < quarterBreakSeconds) return;
                    _quarter++;
                    _clock = quarterSeconds;
                    _phase = Phase.Play;
                    _headline = "QUARTER " + _quarter;
                    _subline = ScoreLine();
                    if (contest) contest.holdRestart = false;
                    return;

                case Phase.FullTime:
                    _breakT += Time.deltaTime;
                    if (_breakT < finalResultSeconds) return;
                    // A drawn final is replayed rather than decided on a coin
                    // toss — simplest thing that is both real footy and not a
                    // dead end for a child.
                    if (HomePoints == AwayPoints) StartFinal(_final);
                    else if (HomePoints > AwayPoints && _final + 1 < series.Length) StartFinal(_final + 1);
                    else
                    {
                        _phase = Phase.SeriesOver;
                        _headline = HomePoints > AwayPoints ? "PREMIERS!" : "SEASON OVER";
                        _subline = HomePoints > AwayPoints
                            ? homeName + " win the flag"
                            : "Tap to start a new season";
                    }
                    return;

                case Phase.SeriesOver:
                    if (Day1Input.TapDown) StartFinal(0);
                    return;
            }
        }

        void EndOfMatch()
        {
            _phase = Phase.FullTime;
            _breakT = 0f;
            if (contest) contest.holdRestart = true;
            if (HomePoints == AwayPoints) { _headline = "DRAW"; _subline = "The " + Title.ToLower() + " is replayed"; }
            else if (HomePoints > AwayPoints)
            {
                _headline = homeName.ToUpper() + " WIN";
                _subline = _final + 1 < series.Length
                    ? "Through to the " + series[_final + 1].title.ToLower()
                    : "Premiers!";
            }
            else { _headline = AwayName.ToUpper() + " WIN"; _subline = "Season over"; }
        }

        string ScoreLine() =>
            $"{homeName} {_homeGoals}.{_homeBehinds} ({HomePoints})   {AwayName} {_awayGoals}.{_awayBehinds} ({AwayPoints})";

        static string QuarterName(int q) => q == 1 ? "QUARTER" : q == 2 ? "HALF" : q == 3 ? "THREE-QUARTER" : "FULL";

        // ── SCOREBOARD ────────────────────────────────────────────────
        // ndw-footy's card, verbatim in its proportions and colours, with the
        // finals title added above it so a child always knows which match this
        // is. Drawn at the TOP; Day1RuckContest's own message sits lower down,
        // so the two never overlap.
        void OnGUI()
        {
            float w = Screen.width, h = Screen.height;
            float u = Mathf.Max(h / 720f, 0.75f);
            var line = new GUIStyle
            {
                fontSize = Mathf.RoundToInt(17f * u),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };

            float cw = Mathf.Min(w * 0.62f, 420f * u), ch = 62f * u;
            float cx = w * 0.5f - cw * 0.5f;
            float top = 24f * u;

            // Which final this is.
            var titleStyle = new GUIStyle(line) { fontSize = Mathf.RoundToInt(15f * u) };
            titleStyle.normal.textColor = new Color(1f, 0.85f, 0.4f);
            GUI.Label(new Rect(cx, 2f, cw, top), Title, titleStyle);

            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(cx, top, cw, ch), Texture2D.whiteTexture);
            GUI.color = Color.white;

            var team = new GUIStyle(line) { alignment = TextAnchor.MiddleLeft, fontSize = Mathf.RoundToInt(16f * u) };
            var tally = new GUIStyle(line) { alignment = TextAnchor.MiddleRight, fontSize = Mathf.RoundToInt(16f * u) };
            var total = new GUIStyle(line) { alignment = TextAnchor.MiddleRight, fontSize = Mathf.RoundToInt(22f * u) };

            bool homeUp = HomePoints >= AwayPoints;
            float pad = 14f * u, rowH = ch * 0.5f;

            team.normal.textColor = homeUp ? new Color(0.45f, 1f, 0.6f) : Color.white;
            total.normal.textColor = team.normal.textColor;
            GUI.Label(new Rect(cx + pad, top, cw, rowH), homeName, team);
            GUI.Label(new Rect(cx, top, cw - pad - 62f * u, rowH), $"{_homeGoals}.{_homeBehinds}", tally);
            GUI.Label(new Rect(cx, top, cw - pad, rowH), $"{HomePoints}", total);

            team.normal.textColor = homeUp ? Color.white : new Color(0.45f, 1f, 0.6f);
            total.normal.textColor = team.normal.textColor;
            GUI.Label(new Rect(cx + pad, top + rowH, cw, rowH), AwayName, team);
            GUI.Label(new Rect(cx, top + rowH, cw - pad - 62f * u, rowH), $"{_awayGoals}.{_awayBehinds}", tally);
            GUI.Label(new Rect(cx, top + rowH, cw - pad, rowH), $"{AwayPoints}", total);

            var clockStyle = new GUIStyle(line) { fontSize = Mathf.RoundToInt(16f * u) };
            clockStyle.normal.textColor = _clock <= 20f && _phase == Phase.Play
                ? new Color(1f, 0.75f, 0.3f) : Color.white;
            int mm = Mathf.FloorToInt(Mathf.Max(0f, _clock) / 60f);
            int ss = Mathf.FloorToInt(Mathf.Max(0f, _clock) % 60f);
            string qLabel = _phase == Phase.QuarterBreak ? QuarterName(_quarter) + " TIME"
                          : _phase == Phase.Play ? $"Q{_quarter}   {mm}:{ss:00}"
                          : "FULL TIME";
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(cx + cw * 0.28f, top + ch, cw * 0.44f, 24f * u), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(cx, top + ch, cw, 24f * u), qLabel, clockStyle);

            if (_phase == Phase.Play) return;

            // Quarter break / full time / premiership card, centred.
            var big = new GUIStyle(line) { fontSize = Mathf.RoundToInt(30f * u) };
            var sub = new GUIStyle(line) { fontSize = Mathf.RoundToInt(16f * u), fontStyle = FontStyle.Normal };
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(w * 0.12f, h * 0.38f, w * 0.76f, 86f * u), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(0f, h * 0.38f + 12f * u, w, 40f * u), _headline, big);
            GUI.Label(new Rect(0f, h * 0.38f + 52f * u, w, 26f * u), _subline, sub);
        }
    }
}
