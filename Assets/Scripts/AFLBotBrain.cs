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

        void Awake()
        {
            _p = GetComponent<AFLPlayer>();
            _home = transform.position;
        }

        void Update()
        {
            if (_p.isUserControlled) return;
            var ball = AFLBall.Instance;
            if (ball == null) return;

            if (_p.HasBall) { WithBall(ball); return; }
            _gotBallAt = 0f;

            if (ball.Carrier != null && ball.Carrier.team != _p.team) { Chase(ball.Carrier.transform.position, ball); return; }
            if (ball.InFlight) { ContestFlight(ball); return; }

            float d = Vector3.Distance(transform.position, ball.transform.position);
            if (d < chaseRadius) Chase(ball.transform.position, ball);
            else MoveTo(_home, _p.walkSpeed);
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
            MoveTo(pos, _p.runSpeed);
            if (Vector3.Distance(transform.position, ball.transform.position) < 1.6f &&
                ball.Carrier == null && Random.value < 0.15f)
                _p.AttemptContest();
        }

        void ContestFlight(AFLBall ball)
        {
            // run to where it's coming down
            Vector3 landing = PredictLanding(ball, transform.position.y);
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
