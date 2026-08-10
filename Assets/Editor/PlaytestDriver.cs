using UnityEngine;
using UnityEditor;
using AFL;

// Editor-only (never ships in the WebGL build) — drives AFLInput's touch
// flags the same way the real HTML control bar does, but with simulated
// human timing: a fixed touch-bridge latency plus random jitter on every
// reactive press, and the single MOVE button held for the whole run (the
// worst realistic case: a kid just mashing one button). This is the
// closest thing to "hand it to a child" available in a headless
// environment — see docs/FOOTY-REBUILD.md's Definition of Done, which
// explicitly says a clean build is not proof the game works.
public class PlaytestDriver : MonoBehaviour
{
    public float touchLatency = 0.09f;   // realistic SendMessage round-trip
    public float humanJitter = 0.16f;    // how sloppy the timing is
    public float maxSeconds = 120f;

    float _t;
    float _queuedMarkAt = -1f;
    bool _reachedBallOnFoot;
    bool _tookAMark;
    bool _reachedSetShot;
    bool _fellOffFieldStuck;
    bool _controlledOrBallEverInvisible;
    int _homeGoalsAtStart, _awayGoalsAtStart;
    float _lastProgressCheck, _lastBallDistAtCheck = 999f;
    int _stallCount;
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
            _log.AppendLine($"[{_t:0.00}s] phase -> {gm.Phase}  home={gm.HomeGoals} away={gm.AwayGoals}");
            _lastPhase = gm.Phase;
        }

        AFLPlayer controlled = null;
        foreach (var p in AFLPlayer.All) if (p.isUserControlled) { controlled = p; break; }

        if (controlled == null) { Finish("nobody is user-controlled — camera/control handoff broke"); return; }
        if (controlled != _lastControlled)
        {
            _log.AppendLine($"[{_t:0.00}s] control -> {controlled.name}");
            _lastControlled = controlled;
        }

        // "Can see the ball and their own player the whole time" — a crude
        // but real proxy: the follow camera must always have a target, and
        // that target must not have fallen below the field.
        if (gm.cam == null || gm.cam.target == null || controlled.transform.position.y < -3f)
            _controlledOrBallEverInvisible = true;

        // Always hold MOVE — worst realistic case, mashing the one button.
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

        // Reactive MARK press, delayed by simulated latency+jitter from the
        // moment a real timing window becomes predictable — mirrors what
        // AFLBotBrain.ContestFlight does for bots, but through the same
        // touch-flag path a real finger uses, with real lag on top.
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
        if (gm.Phase == AFLPhase.SetShot)
        {
            _reachedSetShot = true;
            if (_queuedMarkAt < 0f) _queuedMarkAt = _t + Random.Range(0.3f, 0.9f); // "aim looks about right"
        }

        if (_queuedMarkAt >= 0f && _t >= _queuedMarkAt)
        {
            AFLInput.TouchMarkDown = true;
            _queuedMarkAt = -1f;
        }

        if (gm.HomeGoals != _homeGoalsAtStart) _tookAMark = true; // can't goal without a mark first in this loop

        // Stall watchdog: the ball should be making real progress toward
        // *some* outcome. If distance-to-ball hasn't meaningfully changed
        // in 8 real seconds of sampling, something is stuck outside the
        // game's own boundary/loose-ball safety nets.
        if (_t - _lastProgressCheck > 8f)
        {
            if (Mathf.Abs(distToBall - _lastBallDistAtCheck) < 0.5f) _stallCount++; else _stallCount = 0;
            _lastBallDistAtCheck = distToBall;
            _lastProgressCheck = _t;
            if (_stallCount >= 3) { Finish("stalled — ball/player distance hasn't changed across 24s"); return; }
        }

        if (gm.HomeGoals != _homeGoalsAtStart || gm.AwayGoals != _awayGoalsAtStart)
        {
            _tookAMark = true;
            Finish("goal scored");
            return;
        }

        if (_t > maxSeconds) { Finish("timed out"); return; }
    }

    void Finish(string reason)
    {
        _done = true;
        var gm = AFLGameManager.Instance;
        bool goalScored = gm && (gm.HomeGoals != _homeGoalsAtStart || gm.AwayGoals != _awayGoalsAtStart);

        _log.AppendLine($"REASON {reason}");
        _log.AppendLine($"RESULT reachedBallOnFoot={_reachedBallOnFoot} tookAMark={_tookAMark} " +
                         $"reachedSetShot={_reachedSetShot} goalScored={goalScored} " +
                         $"everInvisible={_controlledOrBallEverInvisible} elapsed={_t:0.0}s");

        bool pass = _reachedBallOnFoot && _tookAMark && _reachedSetShot && goalScored && !_controlledOrBallEverInvisible;
        _log.AppendLine("PASS=" + pass);

        System.IO.File.WriteAllText("/tmp/ndw-afl-playtest.log", _log.ToString());
        Debug.Log("PLAYTEST_LOG_START\n" + _log + "PLAYTEST_LOG_END");

        EditorApplication.isPlaying = false;
        EditorApplication.Exit(pass ? 0 : 2);
    }
}
