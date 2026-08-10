using UnityEngine;

namespace AFL
{
    // Rebuilt 2026-08-11 per issue #1 / docs/FOOTY-REBUILD.md. Replaces the
    // old 4-quarter/clock/behind-tally match sim with the scoped 5-goal
    // chain: centre throw-up -> whoever wins it runs/kicks to a forward
    // option -> forward option contests the mark -> mark taken = set shot,
    // mark lost = straight back to centre. First team to 5 goals wins.
    //
    // Defence uses the SAME mark contest and the SAME MARK button as
    // attack — issue #1's preferred symmetric-scoring option: "one
    // mechanic, two uses, and the kid is never just watching." There is no
    // separate spoil input; a defending player who wins the contest simply
    // denies the attacking team's mark (see OnContestSettled).
    public enum AFLPhase { Centre, OpenPlay, MarkContest, SetShot, Celebration }

    [AddComponentMenu("AFL/AFL Game Manager")]
    public class AFLGameManager : MonoBehaviour
    {
        public static AFLGameManager Instance { get; private set; }
        // Set true only while a set shot is being lined up — every
        // AFLBotBrain checks this and holds still, so the field doesn't
        // keep playing out behind a kid who's lining up their shot.
        public static bool BotsFrozen { get; private set; }

        [Header("Refs")]
        public AFLBall ball;
        public AFLBroadcastCamera cam;
        public Transform centreCircle;
        public Transform goalNorth;   // Home's attacking goal
        public Transform goalSouth;   // Away's attacking goal
        public AFLPlayer.Team userTeam = AFLPlayer.Team.Home;

        [Header("Match")]
        public const int GoalsToWin = 5;
        public float restartDelay = 2.2f;
        public float centreBounceHeight = 8f;

        // Must stay roughly in step with BuildField's Field scale
        // (3.5 x 1 x 4.5 on a 10-unit plane = a 35x45 unit ground).
        [Header("Field bounds")]
        public float fieldHalfWidth = 18f;
        public float fieldHalfLength = 23f;
        public float looseBallTimeout = 4.5f;

        public int HomeGoals { get; private set; }
        public int AwayGoals { get; private set; }
        public AFLPhase Phase { get; private set; } = AFLPhase.Centre;

        AFLPlayer _controlled;
        AFLPlayer.Team _attackingTeam;
        string _message = "";
        float _messageUntil, _scoreLock, _restartAt = -1f, _looseSince = -1f;
        bool _matchOver;

        // set shot state
        AFLPlayer _shotTaker;
        Transform _shotGoal;
        float _shotAimAngle, _shotAimDir = 1f;
        const float ShotAimSweep = 26f;   // degrees either side of straight-at-goal
        const float ShotAimSpeed = 70f;   // degrees/sec

        GUIStyle _big, _small;

        void Awake()
        {
            Instance = this;
            if (!ball) ball = FindAnyObjectByType<AFLBall>();
            if (!cam) cam = FindAnyObjectByType<AFLBroadcastCamera>();
        }

        void Start() { BeginCentre(); }

        void Update()
        {
            if (_matchOver) return;

            CheckBoundary();
            TrackLooseBall();

            switch (Phase)
            {
                case AFLPhase.Centre:   UpdateCentre(); break;
                case AFLPhase.OpenPlay: UpdateOpenPlay(); break;
                case AFLPhase.SetShot:  UpdateSetShot(); break;
                // MarkContest has no per-frame work of its own — it's
                // resolved entirely by OnContestSettled, called out of
                // AFLPlayer.OnContestResult the instant a contest lands.
            }

            if (_restartAt > 0f && Time.time >= _restartAt) BeginCentre();
        }

        // ---------------------------------------------------------------
        //  CENTRE
        // ---------------------------------------------------------------
        void BeginCentre()
        {
            _restartAt = -1f;
            _looseSince = -1f;
            BotsFrozen = false;
            Phase = AFLPhase.Centre;

            Vector3 c = centreCircle ? centreCircle.position : Vector3.zero;
            if (ball) ball.ResetTo(c + Vector3.up * centreBounceHeight);

            var ruck = FindRuck(userTeam);
            if (ruck)
            {
                TakeControl(ruck);
                if (cam) cam.CutTo(ruck.transform);
            }
            Announce("Centre bounce");
        }

        void UpdateCentre()
        {
            if (ball && ball.Carrier != null) OnPossessionGained(ball.Carrier);
        }

        static AFLPlayer FindRuck(AFLPlayer.Team t)
        {
            foreach (var p in AFLPlayer.All) if (p.team == t && p.isRuck) return p;
            return null;
        }

        // ---------------------------------------------------------------
        //  OPEN PLAY — someone has the ball, running/kicking it forward
        // ---------------------------------------------------------------
        void OnPossessionGained(AFLPlayer carrier)
        {
            if (Phase == AFLPhase.OpenPlay && _attackingTeam == carrier.team) return;

            Phase = AFLPhase.OpenPlay;
            _attackingTeam = carrier.team;
            _looseSince = -1f;
            SetAttackTarget(carrier);

            if (carrier.team == userTeam)
            {
                TakeControl(carrier);
                if (cam) cam.CutTo(carrier.transform);
            }
            else
            {
                // Defending: control whichever of ours is nearest the ball,
                // ready to contest the mark when it comes.
                var defender = NearestTeammateToBall(userTeam, null);
                if (defender)
                {
                    TakeControl(defender);
                    if (cam) cam.CutTo(defender.transform);
                }
            }
            Announce((carrier.team == AFLPlayer.Team.Home ? "Crocs" : "Roos") + " have it");
        }

        void UpdateOpenPlay()
        {
            if (ball == null) return;
            if (ball.Carrier) SetAttackTarget(ball.Carrier);

            if (ball.Carrier == null && ball.InFlight && ball.WasKicked)
            {
                Phase = AFLPhase.MarkContest;
                var receiver = NearestTeammateToBall(userTeam, null);
                if (receiver)
                {
                    TakeControl(receiver);
                    if (cam) cam.CutTo(receiver.transform);
                }
                Announce("Contest!");
            }
        }

        // Aim the carrier at a forward option: nearest teammate to their
        // attacking goal, excluding themselves. This is the whole of
        // "curve toward the ball instead of a fixed lane" once you're
        // actually holding the ball — moveTarget just switches from the
        // ball itself (while chasing) to this.
        void SetAttackTarget(AFLPlayer carrier)
        {
            AFLPlayer best = null; float bestD = float.MaxValue;
            Transform goal = carrier.team == AFLPlayer.Team.Home ? goalNorth : goalSouth;
            foreach (var p in AFLPlayer.All)
            {
                if (p.team != carrier.team || p == carrier) continue;
                float d = goal ? (p.transform.position - goal.position).sqrMagnitude
                               : (p.transform.position - carrier.transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = p; }
            }
            carrier.moveTarget = best ? best.transform : goal;
        }

        // ---------------------------------------------------------------
        //  MARK CONTEST result -> set shot or straight back to centre
        // ---------------------------------------------------------------
        public void OnContestSettled(AFLPlayer settledBy, MarkGrade grade)
        {
            if (Phase != AFLPhase.MarkContest) return;

            bool cleanMarkForAttack = (grade == MarkGrade.Screamer || grade == MarkGrade.Clunk)
                                    && settledBy.team == _attackingTeam;

            if (cleanMarkForAttack)
            {
                BeginSetShot(settledBy);
            }
            else
            {
                // Spoiled, fumbled, or intercepted by the defending team —
                // any of those means no shot for whoever was attacking.
                // Issue #1: "mark lost = no shot, straight back to centre."
                Announce("No shot — back to the centre", 2f);
                QueueCentreRestart();
            }
        }

        // ---------------------------------------------------------------
        //  SET SHOT — freeze the field, one swinging arrow, one tap
        // ---------------------------------------------------------------
        void BeginSetShot(AFLPlayer marker)
        {
            Phase = AFLPhase.SetShot;
            BotsFrozen = true;
            _shotTaker = marker;
            _shotGoal = marker.team == AFLPlayer.Team.Home ? goalNorth : goalSouth;
            _shotAimAngle = 0f;
            _shotAimDir = 1f;
            marker.moveTarget = null;
            marker.SetMoveIntent(Vector3.zero, 0f);

            TakeControl(marker);
            if (cam) cam.CutToSetShot(marker.transform, _shotGoal);
            Announce("Set shot!");
        }

        void UpdateSetShot()
        {
            if (_shotTaker == null || _shotGoal == null) { QueueCentreRestart(); return; }

            _shotAimAngle += _shotAimDir * ShotAimSpeed * Time.deltaTime;
            if (_shotAimAngle > ShotAimSweep) { _shotAimAngle = ShotAimSweep; _shotAimDir = -1f; }
            if (_shotAimAngle < -ShotAimSweep) { _shotAimAngle = -ShotAimSweep; _shotAimDir = 1f; }

            if (AFLInput.MarkDown) FireSetShot();
        }

        void FireSetShot()
        {
            Vector3 toGoal = _shotGoal.position - _shotTaker.transform.position; toGoal.y = 0f;
            float dist = toGoal.magnitude;
            toGoal.Normalize();
            Vector3 aimed = Quaternion.Euler(0f, _shotAimAngle, 0f) * toGoal;

            // Power auto-set from distance — the aim arrow is the only
            // decision. Issue #1: "the most forgiving thing in the game."
            float power = Mathf.Clamp01(dist / 28f);
            _shotTaker.Kick(power, aimed);

            BotsFrozen = false;
            Phase = AFLPhase.Celebration;
            QueueCentreRestart();
            Announce("Shot away!");
        }

        // ---------------------------------------------------------------
        //  SCORING  (AFLScoringZone -> here)
        // ---------------------------------------------------------------
        public void RegisterScore(AFLPlayer.Team t, AFLScoringZone.ScoreType type, AFLBall b)
        {
            if (b.Carrier != null) return;
            if (Time.time < _scoreLock) return;
            _scoreLock = Time.time + 2f;

            if (type == AFLScoringZone.ScoreType.Goal)
            {
                if (t == AFLPlayer.Team.Home) HomeGoals++; else AwayGoals++;
                Announce((t == AFLPlayer.Team.Home ? "CROCS" : "ROOS") + " GOAL!");

                if ((t == AFLPlayer.Team.Home ? HomeGoals : AwayGoals) >= GoalsToWin)
                {
                    _matchOver = true;
                    Announce((t == AFLPlayer.Team.Home ? "CROCS WIN!" : "ROOS WIN!"), 999f);
                    return;
                }
            }
            else
            {
                // No separate behind tally — a miss just says so and
                // restarts. Issue #1: delete the behind bookkeeping.
                Announce((t == AFLPlayer.Team.Home ? "Crocs" : "Roos") + " miss — no score");
            }
            QueueCentreRestart();
        }

        // ---------------------------------------------------------------
        //  SAFETY NET — no phase should ever be able to stall the game.
        //  Issue #1: "no boundary rule anywhere... a kick leaving the
        //  plane must never stall the game."
        // ---------------------------------------------------------------
        void CheckBoundary()
        {
            if (!ball || _matchOver || _restartAt > 0f) return;
            Vector3 p = ball.transform.position;
            bool outOfBounds = Mathf.Abs(p.x) > fieldHalfWidth || Mathf.Abs(p.z) > fieldHalfLength || p.y < -4f;
            if (outOfBounds)
            {
                Announce("Out of bounds — ball up", 1.6f);
                QueueCentreRestart(0.6f);
            }
        }

        void TrackLooseBall()
        {
            if (ball == null || Phase == AFLPhase.SetShot) return;
            if (ball.Carrier == null)
            {
                if (_looseSince < 0f) _looseSince = Time.time;
                else if (Time.time - _looseSince > looseBallTimeout && _restartAt < 0f)
                {
                    Announce("Ball up", 1.4f);
                    QueueCentreRestart(0.6f);
                }
            }
            else _looseSince = -1f;
        }

        void QueueCentreRestart(float delay = -1f)
        {
            if (_matchOver) return;
            _restartAt = Time.time + (delay > 0f ? delay : restartDelay);
        }

        // ---------------------------------------------------------------
        //  CONTROL
        // ---------------------------------------------------------------
        public void TakeControl(AFLPlayer p)
        {
            if (!p || p == _controlled) return;
            if (_controlled) _controlled.SetControlled(false);
            _controlled = p;
            _controlled.SetControlled(true);
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

        // ---------------------------------------------------------------
        //  MESSAGES / MINIMAL HUD  — two counters + target, nothing else.
        //  Issue #1: delete the clock/quarters/behind tally and the old
        //  keyboard-hint debug overlay.
        // ---------------------------------------------------------------
        public void AnnounceMark(AFLPlayer p, MarkGrade g)
        {
            Announce(g == MarkGrade.Screamer ? "SPECKY MARK!" : "Mark!");
        }

        public void Announce(string msg, float seconds = 2.5f)
        {
            _message = msg;
            _messageUntil = Time.time + seconds;
        }

        void OnGUI()
        {
            if (_big == null)
            {
                _big = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold };
                _small = new GUIStyle(GUI.skin.label) { fontSize = 15 };
            }

            GUI.Label(new Rect(20, 14, 700, 40),
                string.Format("CROCS {0} — ROOS {1}   ·  first to {2}", HomeGoals, AwayGoals, GoalsToWin), _big);

            if (Time.time < _messageUntil)
                GUI.Label(new Rect(20, 52, 800, 30), _message, _small);

            if (Phase == AFLPhase.SetShot)
            {
                GUI.Label(new Rect(Screen.width / 2f - 110, Screen.height - 70, 260, 26),
                    "aim: " + _shotAimAngle.ToString("+0;-0") + "°  —  tap MARK to kick", _small);
            }
            else if (_controlled != null && _controlled.HasBall && _controlled.KickCharge > 0f)
            {
                GUI.Box(new Rect(20, Screen.height - 46, 240, 22), GUIContent.none);
                GUI.Box(new Rect(22, Screen.height - 44, 236 * _controlled.KickCharge, 18), GUIContent.none);
                GUI.Label(new Rect(270, Screen.height - 48, 300, 24), "Kick power", _small);
            }
        }
    }
}
