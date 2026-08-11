using UnityEngine;
using UnityEditor;
using AFL;

// Editor-only (never ships in the WebGL build) — drives AFLInput's touch
// flags the same way the real HTML control bar does, but with simulated
// human timing: a fixed touch-bridge latency plus random jitter on every
// reactive press, and the single MOVE button held for the whole run (the
// worst realistic case: a kid just mashing one button).
//
// Rewritten 2026-08-11 to specifically chase the reported post-goal
// deadlock (issue #1 comment thread, "verified by playing the live
// build") — logs Phase/_restartAt/matchOver/ball state every frame around
// any goal event, instead of just a single pass/fail at the end.
public class PlaytestDriver : MonoBehaviour
{
    public float touchLatency = 0.09f;
    public float humanJitter = 0.16f;
    public float maxSeconds = 90f;

    float _t;
    float _queuedMarkAt = -1f;
    bool _reachedBallOnFoot;
    bool _sawGoal;
    float _goalAt = -1f;
    int _homeGoalsAtStart, _awayGoalsAtStart;
    readonly System.Text.StringBuilder _log = new System.Text.StringBuilder();
    AFLPlayer _lastControlled;
    AFLPhase _lastPhase;
    bool _done;

    void Start()
    {
        var gm = AFLGameManager.Instance;
        _homeGoalsAtStart = gm ? gm.HomeGoals : 0;
        _awayGoalsAtStart = gm ? gm.AwayGoals : 0;
        _log.AppendLine("PLAYTEST START");
    }

    void Update()
    {
        if (_done) return;
        _t += Time.deltaTime;

        var gm = AFLGameManager.Instance;
        var ball = AFLBall.Instance;
        if (gm == null || ball == null) { Finish("no GameManager/Ball in scene"); return; }

        if (gm.Phase != _lastPhase)
        {
            _log.AppendLine($"[{_t:0.00}s] phase -> {gm.Phase}  home={gm.HomeGoals} away={gm.AwayGoals}  ballPos={ball.transform.position}  ballCarrier={(ball.Carrier ? ball.Carrier.name : "none")}");
            _lastPhase = gm.Phase;
        }

        bool goalJustHappened = (gm.HomeGoals != _homeGoalsAtStart || gm.AwayGoals != _awayGoalsAtStart) && !_sawGoal;
        if (goalJustHappened)
        {
            _sawGoal = true;
            _goalAt = _t;
            _log.AppendLine($"[{_t:0.00}s] GOAL home={gm.HomeGoals} away={gm.AwayGoals} ballPos={ball.transform.position} ballCarrier={(ball.Carrier ? ball.Carrier.name : "none")} phase={gm.Phase}");
        }

        // Detailed post-goal tracing every 0.5s for 15s so a stall is
        // actually visible in the log, not just "test timed out."
        if (_sawGoal && _t - _goalAt < 15f && Mathf.Repeat(_t, 0.5f) < Time.deltaTime * 2f)
        {
            _log.AppendLine($"[{_t:0.00}s] +{(_t - _goalAt):0.0}s post-goal  phase={gm.Phase} ballPos={ball.transform.position} ballCarrier={(ball.Carrier ? ball.Carrier.name : "none")} ballInFlight={ball.InFlight}");
        }

        AFLPlayer controlled = null;
        foreach (var p in AFLPlayer.All) if (p.isUserControlled) { controlled = p; break; }
        if (controlled == null) { Finish("nobody is user-controlled — camera/control handoff broke"); return; }
        if (controlled != _lastControlled)
        {
            _log.AppendLine($"[{_t:0.00}s] control -> {controlled.name}");
            _lastControlled = controlled;
        }

        AFLInput.TouchMoveHeld = true;

        float distToBall = Vector3.Distance(controlled.transform.position, ball.transform.position);
        if (distToBall < 5f) _reachedBallOnFoot = true;

        if (controlled.HasBall)
        {
            AFLInput.TouchKickHeld = true;
            if (controlled.KickCharge > 0.55f)
            {
                AFLInput.TouchKickHeld = false;
                AFLInput.TouchKickUp = true;
            }
        }
        else
        {
            AFLInput.TouchKickHeld = false;
        }

        if (ball.InFlight && _queuedMarkAt < 0f)
        {
            float feet = controlled.transform.position.y;
            float ceiling = feet + controlled.standingReach + controlled.jumpHeight + 0.35f;
            if (ball.PredictReach(controlled.transform.position, Vector3.zero, controlled.catchRadius + 0.7f,
                                   feet + 0.4f, ceiling, 2.5f, out float tHit, out _))
            {
                float pressIn = Mathf.Max(0f, tHit - Random.Range(0f, humanJitter));
                _queuedMarkAt = _t + pressIn + touchLatency;
            }
        }
        if (gm.Phase == AFLPhase.SetShot && _queuedMarkAt < 0f) _queuedMarkAt = _t + Random.Range(0.3f, 0.9f);
        if (_queuedMarkAt >= 0f && _t >= _queuedMarkAt) { AFLInput.TouchMarkDown = true; _queuedMarkAt = -1f; }

        // Stop 12 seconds after the goal — enough to prove either a clean
        // restart or a real stall, without waiting the full maxSeconds.
        if (_sawGoal && _t - _goalAt > 12f) { Finish("12s elapsed after goal"); return; }
        if (_t > maxSeconds) { Finish("timed out"); return; }
    }

    void Finish(string reason)
    {
        _done = true;
        var gm = AFLGameManager.Instance;
        _log.AppendLine($"REASON {reason}");
        _log.AppendLine($"RESULT reachedBallOnFoot={_reachedBallOnFoot} sawGoal={_sawGoal} finalPhase={(gm ? gm.Phase.ToString() : "n/a")} elapsed={_t:0.0}s");
        System.IO.File.WriteAllText("/tmp/ndw-afl-playtest.log", _log.ToString());
        Debug.Log("PLAYTEST_LOG_START\n" + _log + "PLAYTEST_LOG_END");
        EditorApplication.isPlaying = false;
        EditorApplication.Exit(0);
    }
}
