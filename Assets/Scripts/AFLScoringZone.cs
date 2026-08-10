using UnityEngine;

namespace AFL
{
    // =======================================================================
    //  SCORING ZONES  — 4 box triggers between the posts, no rigidbody needed
    // =======================================================================
    [AddComponentMenu("AFL/AFL Scoring Zone")]
    public class AFLScoringZone : MonoBehaviour
    {
        public enum ScoreType { Goal, Behind }
        public ScoreType type = ScoreType.Goal;
        public AFLPlayer.Team scoringTeam = AFLPlayer.Team.Home;

        void Reset()
        {
            var c = GetComponent<Collider>();
            if (c) c.isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            var ball = other.GetComponentInParent<AFLBall>();
            if (ball == null) return;
            AFLGameManager.Instance?.RegisterScore(scoringTeam, type, ball);
        }
    }
}
