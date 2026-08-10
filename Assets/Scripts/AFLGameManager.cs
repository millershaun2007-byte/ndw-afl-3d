using UnityEngine;

namespace AFL
{
    // =======================================================================
    //  GAME MANAGER  — score, clock, centre bounce, player switching, HUD
    // =======================================================================
    [AddComponentMenu("AFL/AFL Game Manager")]
    public class AFLGameManager : MonoBehaviour
    {
        public static AFLGameManager Instance { get; private set; }

        [Header("Refs")]
        public AFLBall ball;
        public AFLBroadcastCamera cam;
        public Transform centreCircle;
        public AFLPlayer.Team userTeam = AFLPlayer.Team.Home;

        [Header("Match")]
        public int quarters = 4;
        public float quarterSeconds = 300f;
        public float bounceDelay = 2.5f;

        public int HomeGoals { get; private set; }
        public int HomeBehinds { get; private set; }
        public int AwayGoals { get; private set; }
        public int AwayBehinds { get; private set; }
        public int Quarter { get; private set; } = 1;
        public float TimeLeft { get; private set; }

        public int HomePoints => HomeGoals * 6 + HomeBehinds;
        public int AwayPoints => AwayGoals * 6 + AwayBehinds;

        AFLPlayer _controlled;
        string _message = "";
        float _messageUntil, _scoreLock, _restartAt = -1f;
        GUIStyle _big, _small;

        void Awake()
        {
            Instance = this;
            TimeLeft = quarterSeconds;
            if (!ball) ball = FindObjectOfType<AFLBall>();
            if (!cam) cam = FindObjectOfType<AFLBroadcastCamera>();
        }

        void Start()
        {
            var first = NearestTeammateToBall(userTeam, null);
            if (first) TakeControl(first);
            if (cam && ball) cam.ball = ball;
        }

        void Update()
        {
            // clock
            if (TimeLeft > 0f)
            {
                TimeLeft -= Time.deltaTime;
                if (TimeLeft <= 0f)
                {
                    TimeLeft = 0f;
                    Announce(Quarter >= quarters ? "FULL TIME" : "End of quarter " + Quarter);
                    if (Quarter < quarters) { Quarter++; TimeLeft = quarterSeconds; QueueCentreBounce(); }
                }
            }

            // auto switch to whoever is closest to the play
            if (AFLInput.Switch)
            {
                var next = NearestTeammateToBall(userTeam, _controlled);
                if (next) TakeControl(next);
            }
            if (ball && ball.Carrier != null && ball.Carrier.team == userTeam && ball.Carrier != _controlled)
                TakeControl(ball.Carrier);   // you always control the player who wins it

            if (_restartAt > 0f && Time.time >= _restartAt) DoCentreBounce();
        }

        public void TakeControl(AFLPlayer p)
        {
            if (_controlled) _controlled.SetControlled(false);
            _controlled = p;
            _controlled.SetControlled(true);
            if (cam) cam.SetTarget(p.transform);
        }

        AFLPlayer NearestTeammateToBall(AFLPlayer.Team t, AFLPlayer exclude)
        {
            if (!ball) return null;
            AFLPlayer best = null; float bestD = float.MaxValue;
            foreach (var p in AFLPlayer.All)
            {
                if (p.team != t || p == exclude) continue;
                float d = (p.transform.position - ball.transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = p; }
            }
            return best;
        }

        public void RegisterScore(AFLPlayer.Team t, AFLScoringZone.ScoreType type, AFLBall b)
        {
            if (b.Carrier != null) return;
            if (Time.time < _scoreLock) return;
            _scoreLock = Time.time + 2f;

            bool cleanGoal = type == AFLScoringZone.ScoreType.Goal
                          && b.WasKicked && !b.HasBounced
                          && b.LastDisposer != null && b.LastDisposer.team == t;

            if (cleanGoal)
            {
                if (t == AFLPlayer.Team.Home) HomeGoals++; else AwayGoals++;
                Announce(t + " GOAL!  6 points");
            }
            else
            {
                if (t == AFLPlayer.Team.Home) HomeBehinds++; else AwayBehinds++;
                Announce(t + " behind  1 point");
            }
            QueueCentreBounce();
        }

        void QueueCentreBounce() { _restartAt = Time.time + bounceDelay; }

        void DoCentreBounce()
        {
            _restartAt = -1f;
            Vector3 c = centreCircle ? centreCircle.position : Vector3.zero;
            if (ball) ball.ResetTo(c + Vector3.up * 8f);   // umpire's bounce
            Announce("Centre bounce");
            var p = NearestTeammateToBall(userTeam, null);
            if (p) TakeControl(p);
        }

        public void AnnounceMark(AFLPlayer p, MarkGrade g)
        {
            Announce(g == MarkGrade.Screamer
                ? "SPECKY!  Mark to " + p.team + "  (" + p.LastTimingError.ToString("+0.00;-0.00") + "s)"
                : "Mark to " + p.team + "  (" + p.LastTimingError.ToString("+0.00;-0.00") + "s)");
        }

        public void Announce(string msg, float seconds = 2.5f)
        {
            _message = msg;
            _messageUntil = Time.time + seconds;
        }

        // ---- quick placeholder HUD; swap for UI Toolkit / uGUI later --------
        void OnGUI()
        {
            if (_big == null)
            {
                _big = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold };
                _small = new GUIStyle(GUI.skin.label) { fontSize = 16 };
            }

            GUI.Label(new Rect(20, 14, 900, 40),
                string.Format("HOME {0}.{1} ({2})     AWAY {3}.{4} ({5})",
                    HomeGoals, HomeBehinds, HomePoints, AwayGoals, AwayBehinds, AwayPoints), _big);

            GUI.Label(new Rect(20, 52, 400, 24),
                string.Format("Q{0}   {1:00}:{2:00}", Quarter, Mathf.FloorToInt(TimeLeft / 60f), Mathf.FloorToInt(TimeLeft % 60f)), _small);

            if (Time.time < _messageUntil)
                GUI.Label(new Rect(20, 78, 800, 30), _message, _small);

            if (_controlled != null && _controlled.HasBall && _controlled.KickCharge > 0f)
            {
                GUI.Box(new Rect(20, Screen.height - 46, 240, 22), GUIContent.none);
                GUI.Box(new Rect(22, Screen.height - 44, 236 * _controlled.KickCharge, 18), GUIContent.none);
                GUI.Label(new Rect(270, Screen.height - 48, 300, 24), "Kick power", _small);
            }

            GUI.Label(new Rect(Screen.width - 300, Screen.height - 110, 300, 110),
                "Hold W to run · Shift sprint\nSpace jump/mark/gather\nLMB hold+release kick\nRMB handball · E tackle · Q switch", _small);
        }
    }
}
