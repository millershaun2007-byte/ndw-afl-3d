using UnityEngine;

namespace AFL
{
    // =======================================================================
    //  BOT BRAIN  — chase, contest with human-ish timing error, kick to goal
    // =======================================================================
    [AddComponentMenu("AFL/AFL Bot Brain")]
    [RequireComponent(typeof(AFLPlayer))]
    public class AFLBotBrain : MonoBehaviour
    {
        [Header("Behaviour")]
        public Transform attackingGoal;
        // Dropped well below the old 0.6 (issue #1: "drop well below" — a
        // bot pays zero input latency for a timed press, a child on a
        // touchscreen pays real round-trip latency through the touch
        // bridge; matching the numbers isn't fair, the bot has to be
        // measurably worse at the same check to stay beatable).
        [Range(0f, 1f)] public float skill = 0.28f;
        public float chaseRadius = 40f;
        public float kickRange = 30f;
        public float holdBeforeDisposal = 0.9f;

        AFLPlayer _p;
        Vector3 _home;
        bool _committed;
        float _recommitAt, _gotBallAt;
        Vector3 _approachOffset;

        void Awake()
        {
            _p = GetComponent<AFLPlayer>();
            _home = transform.position;

            // Small, stable per-bot offset (fixed at spawn) so bots
            // approach a contested ball from spread-out angles instead of
            // all driving toward the exact same point and interpenetrating.
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float radius = Random.Range(0.9f, 1.6f);
            _approachOffset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
        }

        void Update()
        {
            // Every player gets a brain now (BuildScript no longer skips it
            // for the human-designated player) — isUserControlled is the
            // only gate, so whoever control hands away from immediately
            // resumes acting instead of standing frozen. Issue #1:
            // "HomeCroc1 built with no AFLBotBrain... permanently frozen
            // once control leaves it."
            if (_p.isUserControlled) return;
            if (AFLGameManager.BotsFrozen) { _p.SetMoveIntent(Vector3.zero, 0f); return; }

            var ball = AFLBall.Instance;
            if (ball == null) return;

            if (_p.HasBall) { WithBall(ball); return; }
            _gotBallAt = 0f;

            Vector3 focus = ball.Carrier != null ? ball.Carrier.transform.position
                          : (ball.InFlight ? PredictLanding(ball, transform.position.y) : ball.transform.position);
            bool isNearest = IsNearestTeammate(focus);

            if (ball.Carrier != null && ball.Carrier.team == _p.team)
            {
                // A teammate has it — lead toward the attacking goal so the
                // carrier actually has a forward option to run/kick at,
                // instead of everyone bunching around whoever's holding it.
                LeadForGoal();
                return;
            }
            if (ball.Carrier != null && ball.Carrier.team != _p.team)
            {
                if (isNearest) Chase(ball.Carrier.transform.position, ball);
                else HoldShape(focus);
                return;
            }
            if (ball.InFlight)
            {
                if (isNearest) ContestFlight(ball);
                else HoldShape(focus);
                return;
            }

            float d = Vector3.Distance(transform.position, ball.transform.position);
            if (d < chaseRadius)
            {
                if (isNearest) Chase(ball.transform.position, ball);
                else HoldShape(focus);
            }
            else MoveTo(_home, _p.walkSpeed);
        }

        bool IsNearestTeammate(Vector3 point)
        {
            float myDist = Vector3.Distance(transform.position, point);
            foreach (var p in AFLPlayer.All)
            {
                if (p.team != _p.team || p == _p) continue;
                if (Vector3.Distance(p.transform.position, point) < myDist) return false;
            }
            return true;
        }

        // Not directly involved in the current contest — hold a position
        // between home and the play so the team looks alive without
        // swarming the ball.
        void HoldShape(Vector3 focus)
        {
            MoveTo(Vector3.Lerp(_home, focus, 0.35f), _p.walkSpeed);
        }

        void LeadForGoal()
        {
            Vector3 goal = attackingGoal ? attackingGoal.position : transform.position;
            MoveTo(goal, _p.runSpeed * 0.85f);
        }

        void WithBall(AFLBall ball)
        {
            if (_gotBallAt == 0f) _gotBallAt = Time.time;
            Vector3 goal = attackingGoal ? attackingGoal.position : transform.position + transform.forward * 50f;
            Vector3 dir = goal - transform.position; dir.y = 0f;
            float dist = dir.magnitude;

            MoveTo(goal, _p.runSpeed);

            if (Time.time - _gotBallAt > holdBeforeDisposal)
            {
                float power = Mathf.Clamp01(dist / kickRange);
                _p.Kick(Mathf.Lerp(0.35f, 1f, power) * Mathf.Lerp(0.8f, 1f, skill), dir.normalized);
                _gotBallAt = 0f;
            }
        }

        void Chase(Vector3 pos, AFLBall ball)
        {
            float distToBall = Vector3.Distance(transform.position, ball.transform.position);
            MoveTo(pos + _approachOffset, _p.runSpeed);
            if (distToBall < 1.6f && ball.Carrier == null && Random.value < 0.15f)
                _p.AttemptContest();
        }

        void ContestFlight(AFLBall ball)
        {
            Vector3 landing = PredictLanding(ball, transform.position.y) + _approachOffset;
            MoveTo(landing, _p.sprintSpeed);

            if (Time.time > _recommitAt) _committed = false;
            if (_committed) return;

            float feet = transform.position.y;
            float ceiling = feet + _p.standingReach + _p.jumpHeight + 0.35f;
            if (!ball.PredictReach(transform.position, _p.Velocity, _p.catchRadius + 0.6f,
                                   feet + 0.4f, ceiling, 2.5f, out float t, out Vector3 pt)) return;

            bool needsJump = pt.y > feet + _p.standingReach - 0.30f;
            float ideal = needsJump ? _p.TimeToApex : 0.12f;
            float jitter = (1f - skill) * 0.22f;
            float pressAt = ideal + Random.Range(-jitter, jitter);

            if (t <= pressAt)
            {
                _p.AttemptContest();
                _committed = true;
                _recommitAt = Time.time + 1.2f;
            }
        }

        static Vector3 PredictLanding(AFLBall ball, float groundY)
        {
            Vector3 p = ball.Rb.position, v = ball.Rb.linearVelocity;
            const float dt = 0.05f;
            float k = Mathf.Clamp01(1f - ball.drag * dt);
            for (int i = 0; i < 160; i++)
            {
                v += Physics.gravity * dt;
                v *= k;
                p += v * dt;
                if (p.y <= groundY) break;
            }
            p.y = groundY;
            return p;
        }

        void MoveTo(Vector3 worldPos, float speed)
        {
            Vector3 d = worldPos - transform.position; d.y = 0f;
            if (d.sqrMagnitude < 0.6f) { _p.SetMoveIntent(Vector3.zero, 0f); return; }
            _p.SetMoveIntent(d.normalized, speed);
        }
    }
}
