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
        [Range(0f, 1f)] public float skill = 0.6f;      // drives jump-timing error
        public float chaseRadius = 40f;
        public float kickRange = 45f;
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

            // Real bug fix (2026-08-10, direct report — multiple bots
            // visibly merging into one mass around the ball): Chase() and
            // ContestFlight() sent every teammate to the EXACT same point
            // (the ball's literal position / predicted landing spot), so
            // when 2+ bots went for the same loose ball they converged
            // onto identical coordinates and their capsules interpenetrated
            // — CharacterController's own push-apart couldn't keep up
            // against bots continuously re-driving toward that same point
            // every frame. A small, stable per-bot offset (fixed at spawn,
            // not recomputed) means bots approach the ball from spread-out
            // angles instead of all aiming for the same spot — cheap and
            // permanent, doesn't need any flocking/formation logic.
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float radius = Random.Range(0.9f, 1.6f);
            _approachOffset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
        }

        void Update()
        {
            if (_p.isUserControlled) return;
            var ball = AFLBall.Instance;
            if (ball == null) return;

            if (_p.HasBall) { WithBall(ball); return; }
            _gotBallAt = 0f;

            // Real bug fix (2026-08-10, direct report — multiple bots from
            // BOTH teams visibly piling into one mass around the ball):
            // every bot on the field chased any loose ball with no team
            // structure at all — the exact gap the original code review
            // flagged and deferred as "design work, not fixes," but in
            // practice it looks like a monstrous character pile-up, not
            // acceptable to ship. Real fix: only the NEAREST teammate to
            // whatever the ball is doing actually goes for it; everyone
            // else holds a supporting position instead of swarming.
            Vector3 focus = ball.Carrier != null ? ball.Carrier.transform.position
                          : (ball.InFlight ? PredictLanding(ball, transform.position.y) : ball.transform.position);
            bool isNearest = IsNearestTeammate(focus);

            if (ball.Carrier != null && ball.Carrier.team != _p.team)
            {
                if (isNearest) Chase(ball.Carrier.transform.position, ball);
                else Support(focus);
                return;
            }
            if (ball.InFlight)
            {
                if (isNearest) ContestFlight(ball);
                else Support(focus);
                return;
            }

            float d = Vector3.Distance(transform.position, ball.transform.position);
            if (d < chaseRadius)
            {
                if (isNearest) Chase(ball.transform.position, ball);
                else Support(focus);
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

        // Not the nearest teammate to the play — drift partway from home
        // toward it instead of swarming, so the team still looks alive and
        // repositioning without everyone piling onto the same contest.
        void Support(Vector3 focus)
        {
            MoveTo(Vector3.Lerp(_home, focus, 0.35f), _p.walkSpeed);
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
                if (dist < 12f) _p.Handball(dir.normalized);
                else _p.Kick(Mathf.Lerp(0.45f, 1f, power) * Mathf.Lerp(0.8f, 1f, skill), dir.normalized);
                _gotBallAt = 0f;
            }
        }

        void Chase(Vector3 pos, AFLBall ball)
        {
            // Real fix (2026-08-10): used to drop the approach offset once
            // within 2.5 units so bots could actually reach the ball — but
            // that meant the final approach still beelined to the exact
            // same coordinate as the opposing team's nearest player,
            // producing occasional full character interpenetration (caught
            // on a real screenshot, not every run — Unity's per-session
            // Random seed meant it didn't reproduce every time). CanReach()
            // and AttemptTackle() already tolerate ~1.6-1.8 units, well
            // within the 0.9-1.6 unit offset range, so there's no need to
            // ever fully drop it.
            float distToBall = Vector3.Distance(transform.position, ball.transform.position);
            MoveTo(pos + _approachOffset, _p.runSpeed);
            if (distToBall < 1.6f && ball.Carrier == null && Random.value < 0.15f)
                _p.AttemptContest();
        }

        void ContestFlight(AFLBall ball)
        {
            // run to where it's coming down, spread around the landing
            // spot rather than every teammate converging on the identical
            // point (see the Awake() comment on _approachOffset)
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
            Vector3 p = ball.Rb.position, v = ball.Rb.velocity;
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
