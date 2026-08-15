using UnityEngine;

namespace AFL
{
    // =======================================================================
    //  GAME MANAGER — strict beat state machine (2026-08-11 full rewrite)
    // =======================================================================
    // Replaces the free-movement/continuous-contest design entirely, per
    // the rewrite brief on issue #1 after two real human playtests both
    // found MOVE/MARK/KICK effectively non-functional in practice. The
    // whole match is now five discrete beats, each with exactly one live
    // input (a single tap against AFLBeatPrompt's sweep/ring), each with a
    // camera cut, each with players placed at fixed, distinct positions —
    // never navigating toward a shared point, which is what caused the
    // "four arms, two heads" fusing. See AFLBeatPrompt for the shared
    // "one verb" mechanic and AFLPlayer for forward-only movement.
    public enum AFLBeat { RuckTap, RoverChase, ClearanceKick, MarkContest, PlayOnChoice, SnapShot, SetShot, Celebration }

    [AddComponentMenu("AFL/AFL Game Manager")]
    public class AFLGameManager : MonoBehaviour
    {
        public static AFLGameManager Instance { get; private set; }

        [Header("Refs")]
        public AFLBall ball;
        public AFLBroadcastCamera cam;
        public AFLBeatPrompt prompt;
        public Transform centreCircle;
        public Transform goalNorth;   // Home's attacking goal
        public Transform goalSouth;   // Away's attacking goal
        public AFLPlayer.Team userTeam = AFLPlayer.Team.Home;

        [Header("Match")]
        public const int GoalsToWin = 5;
        public float restartDelay = 2.0f;

        [Header("Field bounds — also used to keep the camera off the edge")]
        public float fieldHalfWidth = 18f;
        public float fieldHalfLength = 23f;

        // Watchdog: issue #1 section 7 — "any beat that has not advanced
        // within a few seconds forces a reset to the centre throw-up."
        public float beatWatchdogSeconds = 7f;

        public int HomeGoals { get; private set; }
        public int AwayGoals { get; private set; }
        public AFLBeat Beat { get; private set; }

        AFLPlayer.Team _actingTeam;   // whose chain this is
        AFLPlayer _ruck, _receiver, _shooter, _defender, _tackler;
        Vector3 _clearanceAimTarget;   // world position the clearance kick aims toward
        string _message = "";
        float _messageUntil, _restartAt = -1f, _beatStartedAt, _scoreLock;
        bool _matchOver;

        GUIStyle _big, _small;

        void Awake()
        {
            Instance = this;
            if (!ball) ball = FindAnyObjectByType<AFLBall>();
            if (!cam) cam = FindAnyObjectByType<AFLBroadcastCamera>();
            if (!prompt) prompt = FindAnyObjectByType<AFLBeatPrompt>();
        }

        void Start()
        {
            _actingTeam = userTeam;
            BeginRuckTap();
        }

        void Update()
        {
            if (_matchOver) return;

            EnforceInvariants();

            // Watchdog — no beat should ever be able to stall the game.
            if (_restartAt < 0f && Time.time - _beatStartedAt > beatWatchdogSeconds)
            {
                Announce("Taking too long — resetting", 1.6f);
                QueueRestart(0.4f);
            }

            switch (Beat)
            {
                case AFLBeat.RuckTap: UpdateRuckTapBeat(); break;
                case AFLBeat.RoverChase: UpdateRoverChaseBeat(); break;
                case AFLBeat.ClearanceKick: UpdateClearanceKickBeat(); break;
                case AFLBeat.MarkContest: UpdateMarkContestBeat(); break;
                case AFLBeat.PlayOnChoice: UpdatePlayOnChoiceBeat(); break;
                case AFLBeat.SnapShot: UpdateSnapShotBeat(); break;
                case AFLBeat.SetShot: UpdateSetShotBeat(); break;
            }

            if (_restartAt > 0f && Time.time >= _restartAt)
            {
                _actingTeam = _actingTeam == AFLPlayer.Team.Home ? AFLPlayer.Team.Away : AFLPlayer.Team.Home;
                BeginRuckTap();
            }
        }

        // ---------------------------------------------------------------
        //  INVARIANTS — issue #1 section 7: ball and every player must
        //  always be above ground and inside bounds, or get reset. This is
        //  the structural fix for "ran off the edge of the ground plane
        //  and fell" — a backstop in case the physical boundary walls
        //  (BuildScript) are ever bypassed by a fast-enough kick.
        // ---------------------------------------------------------------
        void EnforceInvariants()
        {
            if (ball)
            {
                Vector3 p = ball.transform.position;
                bool bad = Mathf.Abs(p.x) > fieldHalfWidth + 1f || Mathf.Abs(p.z) > fieldHalfLength + 1f || p.y < -3f;
                if (bad && ball.Carrier == null && _restartAt < 0f)
                {
                    Announce("Out of bounds — ball up", 1.4f);
                    QueueRestart(0.4f);
                }
            }

            foreach (var p in AFLPlayer.All)
            {
                Vector3 pos = p.transform.position;
                bool bad = Mathf.Abs(pos.x) > fieldHalfWidth + 2f || Mathf.Abs(pos.z) > fieldHalfLength + 2f || pos.y < -3f;
                if (bad)
                {
                    Vector3 safe = ClampInsideField(pos);
                    safe.y = 1f;
                    TeleportPlayer(p, safe);
                }
            }
        }

        Vector3 ClampInsideField(Vector3 p)
        {
            p.x = Mathf.Clamp(p.x, -fieldHalfWidth + 2f, fieldHalfWidth - 2f);
            p.z = Mathf.Clamp(p.z, -fieldHalfLength + 2f, fieldHalfLength - 2f);
            return p;
        }

        static void TeleportPlayer(AFLPlayer p, Vector3 pos)
        {
            var cc = p.GetComponent<CharacterController>();
            if (cc) cc.enabled = false;
            p.transform.position = pos;
            if (cc) cc.enabled = true;
        }

        // ---------------------------------------------------------------
        //  BEAT 1 — RUCK TAP: sweep arrow picks which rover gets the ball.
        // ---------------------------------------------------------------
        AFLPlayer _leftOption, _rightOption;

        void BeginRuckTap()
        {
            _restartAt = -1f;
            _beatStartedAt = Time.time;
            _botCommitAt = -1f;
            Beat = AFLBeat.RuckTap;

            _ruck = FindRuck(_actingTeam);
            var teammates = TeammatesOf(_actingTeam, _ruck);
            _leftOption = teammates.Count > 0 ? teammates[0] : _ruck;
            _rightOption = teammates.Count > 1 ? teammates[1] : _ruck;

            Vector3 c = centreCircle ? centreCircle.position : Vector3.zero;
            Vector3 attackDir = _actingTeam == AFLPlayer.Team.Home ? Vector3.forward : Vector3.back;

            PlaceAtSlot(_ruck, c, attackDir);
            PlaceAtSlot(_leftOption, c + Cross(attackDir) * -6f + attackDir * 6f, attackDir);
            PlaceAtSlot(_rightOption, c + Cross(attackDir) * 6f + attackDir * 6f, attackDir);
            SpreadRemainingPlayers(c, attackDir);

            if (ball) ball.ResetTo(c + Vector3.up * 1.2f);

            TakeBeatControl(_ruck);
            if (cam) cam.CutTo(_ruck.transform, attackDir);

            if (prompt) prompt.BeginSweep("Tap MARK: aim the ruck tap", 0f);
            Announce("Ruck tap — " + TeamName(_actingTeam));
        }

        void UpdateRuckTapBeat()
        {
            if (!ResolveSingleTap(_ruck)) return;
            var (grade, _) = prompt.Resolve();
            prompt.Stop();

            // Sweep value picks left vs right option; grade decides how
            // cleanly it arrives (a poor tap still goes somewhere playable
            // — issue #1: nothing here should be able to just fail dead).
            AFLPlayer target = prompt.CurrentValue < 0f ? _leftOption : _rightOption;
            _receiver = target ? target : _ruck;

            if (ball) ball.Attach(_receiver);
            AnnounceGrade("Tap", grade);
            BeginRoverChase();
        }

        // ---------------------------------------------------------------
        //  BEAT 1.5 — ROVER CHASE: the defending team's rover chases the
        //  ball carrier from behind in the same straight lane and tries to
        //  tackle them before they can dispose of it. Same dual-tap-contest
        //  shape as MarkContest (one verb, whoever's not the human is bot-
        //  timed) — the difference is both players actually run this beat
        //  (SetMoveHeld(true) for both, not just one), so it reads as a
        //  real chase rather than two players standing still waiting to
        //  jump. Still straight-line-only — the tackler starts behind the
        //  carrier in the exact same lane, so "catching up" is a speed/
        //  timing question, never a steering one (see AFLPlayer's own
        //  class comment on why free steering was removed).
        // ---------------------------------------------------------------
        const float ChaseGapStart = 7f;

        void BeginRoverChase()
        {
            _beatStartedAt = Time.time;
            _botCommitAt = -1f;
            Beat = AFLBeat.RoverChase;

            var defendingTeam = _actingTeam == AFLPlayer.Team.Home ? AFLPlayer.Team.Away : AFLPlayer.Team.Home;
            _tackler = FindRuck(defendingTeam) ?? _ruck;

            Vector3 c = centreCircle ? centreCircle.position : Vector3.zero;
            Vector3 attackDir = _actingTeam == AFLPlayer.Team.Home ? Vector3.forward : Vector3.back;

            PlaceAtSlot(_receiver, c, attackDir);
            PlaceAtSlot(_tackler, c - attackDir * ChaseGapStart, attackDir);

            // Both run this beat — unlike TakeBeatControl elsewhere (which
            // hands movement to exactly one bot), a chase needs the
            // carrier fleeing AND the tackler pursuing at the same time.
            foreach (var pl in AFLPlayer.All) if (!pl.isUserControlled) pl.SetMoveHeld(false);
            if (_receiver && !_receiver.isUserControlled) _receiver.SetMoveHeld(true);
            if (_tackler && !_tackler.isUserControlled) _tackler.SetMoveHeld(true);

            if (cam) cam.CutToSide(_receiver.transform, attackDir);

            bool userIsCarrier = _actingTeam == userTeam;
            if (prompt) { prompt.ringDuration = 2.0f; prompt.BeginRing(userIsCarrier ? "Tap MARK to sprint clear!" : "Tap MARK to lunge for the tackle!"); }
            Announce("Chase!");
        }

        void UpdateRoverChaseBeat()
        {
            if (!prompt.IsLive) return;

            bool carrierTapped = false, tacklerTapped = false;
            if (_actingTeam == userTeam) { carrierTapped = AFLInput.MarkDown; tacklerTapped = BotTap(_tackler); }
            else { tacklerTapped = AFLInput.MarkDown; carrierTapped = BotTap(_receiver); }

            if (!carrierTapped && !tacklerTapped && prompt.CurrentValue < 1f) return;

            var (grade, _) = carrierTapped || tacklerTapped ? prompt.Resolve() : (0.05f, 0f);
            prompt.Stop();

            bool carrierEscaped = carrierTapped && grade >= 0.30f;
            bool tackled = tacklerTapped && grade >= 0.45f && !carrierEscaped;

            if (tackled)
            {
                if (_tackler) _tackler.Tackle();
                if (_receiver) _receiver.GetTackled();
                if (cam) cam.Punch();
                AnnounceGrade("TACKLED! Turnover", grade);
                if (ball) ball.Spoil(_tackler ? _tackler.transform.position : _receiver.transform.position);
                QueueRestart();
            }
            else
            {
                // Anything short of a clean tackle and the carrier keeps
                // the ball — a near-miss lunge doesn't need its own losing
                // state, same simplification MarkContest's "clean drop"
                // case makes for a poorly-timed spoil attempt.
                AnnounceGrade(carrierEscaped ? "Breaks clear!" : "Shrugs off the chase", grade);
                BeginClearanceKick();
            }
        }

        // ---------------------------------------------------------------
        //  BEAT 2 — CLEARANCE KICK: same arrow, power auto-set by distance.
        // ---------------------------------------------------------------
        AFLPlayer _forwardOption;

        void BeginClearanceKick()
        {
            _beatStartedAt = Time.time;
            _botCommitAt = -1f;
            Beat = AFLBeat.ClearanceKick;

            var teammates = TeammatesOf(_actingTeam, _receiver);
            _forwardOption = teammates.Count > 0 ? teammates[0] : _receiver;

            Vector3 attackDir = _actingTeam == AFLPlayer.Team.Home ? Vector3.forward : Vector3.back;
            _receiver.SnapFacing(attackDir);

            TakeBeatControl(_receiver);
            if (cam) cam.CutToSide(_receiver.transform, attackDir);

            if (prompt) prompt.BeginSweep("Tap MARK: kick it forward", 0f);
            Announce(TeamName(_actingTeam) + " clear it");
        }

        void UpdateClearanceKickBeat()
        {
            if (_receiver) _receiver.SetKickChargeVisual(Mathf.InverseLerp(-1f, 1f, prompt.CurrentValue));
            if (!ResolveSingleTap(_receiver)) return;
            var (grade, _) = prompt.Resolve();
            prompt.Stop();

            Vector3 attackDir = _actingTeam == AFLPlayer.Team.Home ? Vector3.forward : Vector3.back;
            // Aim comes entirely from the arrow now, never facing (issue
            // #1 section 4) — a small yaw offset driven by CurrentValue,
            // clean grade = straighter, poor grade = more skew.
            float skewDeg = prompt.CurrentValue * 22f;
            Vector3 aim = Quaternion.Euler(0f, skewDeg, 0f) * attackDir;

            _receiver.Kick(Mathf.Lerp(0.55f, 1f, grade), aim);
            AnnounceGrade("Kick", grade);

            Transform goal = _actingTeam == AFLPlayer.Team.Home ? goalNorth : goalSouth;
            _clearanceAimTarget = _forwardOption ? _forwardOption.transform.position
                                                  : (goal ? goal.position : _receiver.transform.position + aim * 15f);
            BeginMarkContest();
        }

        // ---------------------------------------------------------------
        //  BEAT 3 — MARK CONTEST: closing ring on the descending ball.
        //  Attacker taps for a mark, defender's bot/human taps to spoil —
        //  same ring, same rules, whichever side times it closer wins
        //  (issue #1: defence reuses the identical mechanic).
        // ---------------------------------------------------------------
        void BeginMarkContest()
        {
            _beatStartedAt = Time.time;
            _botCommitAt = -1f;
            Beat = AFLBeat.MarkContest;

            var defendingTeam = _actingTeam == AFLPlayer.Team.Home ? AFLPlayer.Team.Away : AFLPlayer.Team.Home;
            _defender = FindRuck(defendingTeam) ?? _ruck;

            Vector3 landing = _clearanceAimTarget;
            Vector3 attackDir = _actingTeam == AFLPlayer.Team.Home ? Vector3.forward : Vector3.back;

            if (_forwardOption) PlaceAtSlot(_forwardOption, landing, attackDir);
            PlaceAtSlot(_defender, landing + Cross(attackDir) * 1.6f, -attackDir);

            TakeBeatControl(_actingTeam == userTeam ? _forwardOption : _defender);
            if (cam) cam.CutToSide((_forwardOption ? _forwardOption.transform : _defender.transform), attackDir);

            float fallTime = ball ? Mathf.Clamp(ball.EstimateFallTime(landing.y + 3f), 0.8f, 2.4f) : 1.4f;
            if (prompt) { prompt.ringDuration = fallTime; prompt.BeginRing("Tap MARK as the ring closes!"); }
            Announce("Contest!");
        }

        void UpdateMarkContestBeat()
        {
            if (!prompt.IsLive) return;

            bool attackerTapped = false, defenderTapped = false;
            if (_actingTeam == userTeam) { attackerTapped = AFLInput.MarkDown; defenderTapped = BotTap(_defender); }
            else { defenderTapped = AFLInput.MarkDown; attackerTapped = BotTap(_forwardOption); }

            // First side to actually tap resolves the contest — matches a
            // real jump-for-it feel (whoever commits first gets graded);
            // if the ring finishes with nobody tapping, that's a clean drop.
            if (!attackerTapped && !defenderTapped && prompt.CurrentValue < 1f) return;

            var (grade, _) = attackerTapped || defenderTapped ? prompt.Resolve() : (0.05f, 0f);
            prompt.Stop();

            bool attackerWon = attackerTapped && grade >= 0.30f;
            bool defenderSpoiled = defenderTapped && grade >= 0.45f && !attackerWon;

            if (attackerWon)
            {
                if (_forwardOption) { _forwardOption.Jump(); ball.Attach(_forwardOption); }
                AnnounceGrade(grade >= 0.85f ? "SPECKY MARK!" : "Mark!", grade);
                BeginPlayOnChoice(_forwardOption ?? _receiver);
            }
            else if (defenderSpoiled)
            {
                if (_defender) _defender.Spoil();
                if (cam) cam.Punch();
                Announce("Spoiled! Back to the centre", 1.8f);
                if (ball) ball.Spoil(_defender ? _defender.transform.position : _clearanceAimTarget);
                QueueRestart();
            }
            else
            {
                Announce("Dropped — back to the centre", 1.8f);
                if (ball) ball.ResetTo(_clearanceAimTarget + Vector3.up * 0.5f);
                QueueRestart();
            }
        }

        // Bots share the identical visible cue rather than a hidden
        // calculation — issue #1 section 6. Reaction delay is deliberately
        // worse than the touch bridge's own round trip, and skill sits
        // below the perfect band, so a bot is beatable, not just "fair."
        // One shared timer slot is enough: RuckTap/ClearanceKick/SetShot
        // only ever have one relevant player (human XOR bot), and
        // MarkContest's two calls (attacker/defender) only ever have one
        // bot side active at a time too, since TakeBeatControl always
        // hands control to exactly one of the two.
        float _botCommitAt = -1f;
        bool BotTap(AFLPlayer bot)
        {
            if (!bot || bot.isUserControlled) return false;
            if (_botCommitAt < 0f)
            {
                float reactionDelay = 0.22f + Random.Range(0f, 0.25f);   // worse than real touch latency
                _botCommitAt = _beatStartedAt + reactionDelay + Random.Range(0.15f, 0.55f);
            }
            if (Time.time >= _botCommitAt) { _botCommitAt = -1f; return true; }
            return false;
        }

        // Single-relevant-player beats (RuckTap/ClearanceKick/PlayOnChoice/
        // SnapShot/SetShot): human presses MARK if it's their player,
        // otherwise resolves on the same bot timer as MarkContest.
        bool ResolveSingleTap(AFLPlayer relevant)
        {
            if (relevant && relevant.isUserControlled) return AFLInput.MarkDown;
            return BotTap(relevant);
        }

        // ---------------------------------------------------------------
        //  BEAT 3.5 — PLAY ON CHOICE: after a successful mark, the same
        //  left/right sweep RuckTap already uses to pick a rover now picks
        //  between the two things a real mark actually lets you do — take
        //  the set shot (line up properly, slower, more accurate) or play
        //  on and snap it immediately (rushed, less accurate, but the
        //  defence never gets a chance to reset). Same single verb as
        //  everywhere else; the sweep's side is the whole decision.
        // ---------------------------------------------------------------
        void BeginPlayOnChoice(AFLPlayer marker)
        {
            _beatStartedAt = Time.time;
            _botCommitAt = -1f;
            Beat = AFLBeat.PlayOnChoice;
            _shooter = marker;

            TakeBeatControl(marker);
            if (cam) cam.CutToSide(marker.transform, marker.transform.forward);

            if (prompt) prompt.BeginSweep("Tap MARK: LEFT play on & snap! · RIGHT set shot!", 0f);
            Announce("Play on, or set shot?");
        }

        void UpdatePlayOnChoiceBeat()
        {
            if (!ResolveSingleTap(_shooter)) return;
            bool playOn = prompt.CurrentValue < 0f;
            prompt.Stop();

            if (playOn) BeginSnapShot(_shooter);
            else BeginSetShot(_shooter);
        }

        // ---------------------------------------------------------------
        //  BEAT 3.6 — SNAP SHOT: playing on, not squaring up to goal first
        //  (no SnapFacing call — kicks from however they're already facing
        //  out of the mark, same attackDir they were placed in for the
        //  contest). Faster sweep (harder to time) and a wider aim skew
        //  plus a lower power ceiling than a set shot — a real snap is
        //  genuinely less reliable, that's the whole trade-off for taking
        //  it quickly instead of setting up properly.
        // ---------------------------------------------------------------
        void BeginSnapShot(AFLPlayer marker)
        {
            _beatStartedAt = Time.time;
            _botCommitAt = -1f;
            Beat = AFLBeat.SnapShot;
            _shooter = marker;

            TakeBeatControl(marker);
            Transform goal = _actingTeam == AFLPlayer.Team.Home ? goalNorth : goalSouth;
            Vector3 toGoal = goal ? (goal.position - marker.transform.position) : marker.transform.forward;
            toGoal.y = 0f; toGoal.Normalize();
            if (cam) cam.CutToSide(marker.transform, toGoal);

            if (prompt) { prompt.sweepSpeed = 2.6f; prompt.BeginSweep("Tap MARK: SNAP shot, quick!", 0f); }
            Announce("Playing on!");
        }

        void UpdateSnapShotBeat()
        {
            if (_shooter) _shooter.SetKickChargeVisual(Mathf.InverseLerp(-1f, 1f, prompt.CurrentValue));
            if (!ResolveSingleTap(_shooter)) return;
            var (grade, _) = prompt.Resolve();
            prompt.Stop();
            // Reset immediately after use — this is the one beat that ever
            // touches AFLBeatPrompt's shared sweepSpeed, so resetting it
            // right here (rather than scattering a defensive reset across
            // every other Begin* method) is the whole fix; every future
            // sweep beat starts clean regardless of how this one resolved.
            prompt.sweepSpeed = 1.6f;

            Transform goal = _actingTeam == AFLPlayer.Team.Home ? goalNorth : goalSouth;
            Vector3 toGoal = goal ? (goal.position - _shooter.transform.position) : _shooter.transform.forward;
            toGoal.y = 0f; toGoal.Normalize();
            float skewDeg = prompt.CurrentValue * 30f;
            Vector3 aimed = Quaternion.Euler(0f, skewDeg, 0f) * toGoal;

            _shooter.Kick(Mathf.Lerp(0.4f, 0.85f, grade), aimed);
            AnnounceGrade("Snap shot away", grade);
            Beat = AFLBeat.Celebration;
            QueueRestart();
        }

        // ---------------------------------------------------------------
        //  BEAT 4 — SET SHOT: the one mechanic that already worked —
        //  reused as the template for every other beat above.
        // ---------------------------------------------------------------
        void BeginSetShot(AFLPlayer marker)
        {
            _beatStartedAt = Time.time;
            _botCommitAt = -1f;
            Beat = AFLBeat.SetShot;
            _shooter = marker;

            Transform goal = _actingTeam == AFLPlayer.Team.Home ? goalNorth : goalSouth;
            Vector3 toGoal = goal ? (goal.position - marker.transform.position) : marker.transform.forward;
            toGoal.y = 0f; toGoal.Normalize();
            marker.SnapFacing(toGoal);

            TakeBeatControl(marker);
            if (cam) cam.CutToSide(marker.transform, toGoal);

            if (prompt) prompt.BeginSweep("Tap MARK: shot at goal!", 0f);
            Announce("Set shot!");
        }

        void UpdateSetShotBeat()
        {
            if (_shooter) _shooter.SetKickChargeVisual(Mathf.InverseLerp(-1f, 1f, prompt.CurrentValue));
            if (!ResolveSingleTap(_shooter)) return;
            var (grade, _) = prompt.Resolve();
            prompt.Stop();

            Transform goal = _actingTeam == AFLPlayer.Team.Home ? goalNorth : goalSouth;
            Vector3 toGoal = goal ? (goal.position - _shooter.transform.position) : _shooter.transform.forward;
            toGoal.y = 0f; toGoal.Normalize();
            float skewDeg = prompt.CurrentValue * 18f;
            Vector3 aimed = Quaternion.Euler(0f, skewDeg, 0f) * toGoal;

            _shooter.Kick(Mathf.Lerp(0.6f, 1f, grade), aimed);
            AnnounceGrade("Shot away", grade);
            Beat = AFLBeat.Celebration;
            QueueRestart();
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
                    if (prompt) prompt.Stop();
                    Announce((t == AFLPlayer.Team.Home ? "CROCS WIN!" : "ROOS WIN!"), 999f);
                    return;
                }
            }
            else
            {
                Announce((t == AFLPlayer.Team.Home ? "Crocs" : "Roos") + " miss — no score");
            }
            if (_restartAt < 0f) QueueRestart();
        }

        // ---------------------------------------------------------------
        //  HELPERS
        // ---------------------------------------------------------------
        static AFLPlayer FindRuck(AFLPlayer.Team t)
        {
            foreach (var p in AFLPlayer.All) if (p.team == t && p.isRuck) return p;
            foreach (var p in AFLPlayer.All) if (p.team == t) return p;
            return null;
        }

        static System.Collections.Generic.List<AFLPlayer> TeammatesOf(AFLPlayer.Team t, AFLPlayer exclude)
        {
            var list = new System.Collections.Generic.List<AFLPlayer>();
            foreach (var p in AFLPlayer.All) if (p.team == t && p != exclude) list.Add(p);
            return list;
        }

        static Vector3 Cross(Vector3 flatDir) => Vector3.Cross(Vector3.up, flatDir).normalized;

        static void PlaceAtSlot(AFLPlayer p, Vector3 pos, Vector3 facing)
        {
            if (!p) return;
            pos.y = 1f;
            TeleportPlayer(p, pos);
            p.SnapFacing(facing);
        }

        // Every player not actively involved in this beat still gets a
        // distinct, spread-out slot — never the same point as anyone else
        // (issue #1 section 8: "distinct destination slots per player
        // rather than a shared target point" is what actually fixes the
        // fusing, not collision alone).
        void SpreadRemainingPlayers(Vector3 focus, Vector3 attackDir)
        {
            int i = 0;
            foreach (var p in AFLPlayer.All)
            {
                if (p == _ruck || p == _leftOption || p == _rightOption) continue;
                float side = (i % 2 == 0) ? 1f : -1f;
                float back = 4f + i * 2f;
                Vector3 slot = focus + Cross(attackDir) * side * (8f + i) - attackDir * back;
                PlaceAtSlot(p, slot, attackDir);
                i++;
            }
        }

        // Drives forward-advance for whichever player is active this beat,
        // WITHOUT touching isUserControlled — that flag is fixed at spawn
        // (BuildScript) and must stay a stable "is this Shaun's own
        // character" fact, not something that flips depending on whose
        // beat it currently is. A bot's own movement never reads
        // AFLInput, so it needs an explicit push here; the human's own
        // player already reads AFLInput.MoveHeld directly in
        // AFLPlayer.Update() and needs nothing from this method at all.
        void TakeBeatControl(AFLPlayer active)
        {
            foreach (var pl in AFLPlayer.All) if (!pl.isUserControlled) pl.SetMoveHeld(false);
            if (active && !active.isUserControlled) active.SetMoveHeld(true);
        }

        void QueueRestart(float delay = -1f)
        {
            if (_matchOver) return;
            if (prompt) prompt.Stop();
            _restartAt = Time.time + (delay > 0f ? delay : restartDelay);
        }

        static string TeamName(AFLPlayer.Team t) => t == AFLPlayer.Team.Home ? "Crocs" : "Roos";

        void AnnounceGrade(string label, float grade)
        {
            string quality = grade >= 0.85f ? "Perfect!" : grade >= 0.6f ? "Good" : grade >= 0.3f ? "OK" : "Scrappy";
            Announce($"{label} — {quality}");
        }

        public void Announce(string msg, float seconds = 2.2f)
        {
            _message = msg;
            _messageUntil = Time.time + seconds;
        }

        // ---------------------------------------------------------------
        //  HUD — issue #1 section 9: one line, large, high contrast, dark
        //  panel behind it, size derived from viewport height. The beat
        //  prompt itself (arrow/ring) is drawn by AFLBeatPrompt; this is
        //  just the score line and the transient status message.
        // ---------------------------------------------------------------
        void OnGUI()
        {
            EnsureStyles();

            int pad = Mathf.RoundToInt(Screen.height * 0.02f);
            int scoreH = Mathf.RoundToInt(Screen.height * 0.08f);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(pad, pad, Screen.width - pad * 2, scoreH), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(pad, pad, Screen.width - pad * 2, scoreH),
                $"CROCS {HomeGoals} — ROOS {AwayGoals}   ·  first to {GoalsToWin}", _big);

            if (Time.time < _messageUntil)
            {
                int msgY = pad * 2 + scoreH;
                int msgH = Mathf.RoundToInt(Screen.height * 0.055f);
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(new Rect(pad, msgY, Screen.width - pad * 2, msgH), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(pad, msgY, Screen.width - pad * 2, msgH), _message, _small);
            }
        }

        void EnsureStyles()
        {
            if (_big != null) return;
            _big = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(Screen.height * 0.05f),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Color.white }
            };
            _small = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(Screen.height * 0.035f),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(1f, 0.9f, 0.5f) }
            };
        }
    }
}
