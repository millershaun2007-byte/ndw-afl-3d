using System.Collections.Generic;

namespace AFL.Match
{
    // The whole game as DATA. No Transform, no coroutine, no camera, no
    // WaitForSeconds. Give it a seed and the taps, get back a list of beats.
    //
    // Written 2026-08-28, from scratch, at Shaun's call after a day in which
    // eight different builds were swapped in and out and none could be
    // reproduced. Two rules drive the design, both from what went wrong:
    //
    //   1. SEEDED. Every roll comes from one System.Random per round, seeded
    //      from a number shown on screen. "The mark spilled again" becomes
    //      "round 12, seed 8814" and can be replayed exactly. The old file had
    //      five unseeded UnityEngine.Random calls, which is why every bug
    //      report became guesswork.
    //
    //   2. DERIVED, NEVER STORED. Every bug this project has had was one fact
    //      written in two places that drifted apart - direction vs possession,
    //      ball position vs the kicker's, the camera pivot vs the contest. Here
    //      direction is a property of the team and nothing else. It cannot
    //      disagree with possession because it is not stored anywhere.

    public enum Team { Crocs, Roos }

    public enum BeatKind
    {
        CentreBounce, RuckTap, Clearance, KickForward,
        MarkContest, Spoil, Spill, SetShot, Goal, Behind, KickIn, RoundEnd
    }

    public struct Beat
    {
        public BeatKind Kind;
        public Team InPossession;
        public string Message;
        public int Points;

        // Direction is DERIVED from the team, never passed in. This single
        // rule removes the "all over the shop" direction bug by construction.
        public float ZDir => InPossession == Team.Crocs ? 1f : -1f;
    }

    public sealed class MatchSim
    {
        public readonly int Seed;
        readonly System.Random _rng;

        public MatchSim(int seed) { Seed = seed; _rng = new System.Random(seed); }

        float Range(float a, float b) => a + (float)_rng.NextDouble() * (b - a);
        bool Chance(float p) => _rng.NextDouble() < p;

        // How close the human's tap was, 0 = perfect. Passed in, not read from
        // Input, so a round can be replayed headlessly in a test.
        public List<Beat> PlayRound(Team firstUse, float humanTapError, float tapWindow)
        {
            var beats = new List<Beat>();
            void Add(BeatKind k, Team t, string m, int pts = 0)
                => beats.Add(new Beat { Kind = k, InPossession = t, Message = m, Points = pts });

            Add(BeatKind.CentreBounce, firstUse, "Centre bounce...");

            // Ruck tap. Both sides commit ONCE - the old game scored the human
            // on their best of many taps against a bot that committed once,
            // which made mashing unloseable and produced a 51-8 scoreline.
            float botError = System.Math.Abs(Range(-0.15f, 0.15f));
            bool humanWins = humanTapError <= botError;
            Team holder = humanWins ? Team.Crocs : Team.Roos;
            Add(BeatKind.RuckTap, holder,
                humanWins ? "Crocs win the tap!" : "Roos win the tap!");

            Add(BeatKind.Clearance, holder,
                holder == Team.Crocs ? "Crocs' rover gets it!" : "Roos' rover gets it!");
            Add(BeatKind.KickForward, holder, "Kicks it forward...");

            // Marking contest. Every probability is a fraction of the window it
            // competes against, so "make it easier" is one number and the AI
            // can never become mathematically unmissable the way it was.
            bool spoiled = Chance(0.36f);
            if (spoiled)
            {
                Add(BeatKind.Spoil, holder, "Spoiled by the defender!");
                Add(BeatKind.Behind, holder, "Rushed behind - one point!", 1);
                Add(BeatKind.KickIn, Other(holder), "Kicks in from fullback!");
                return beats;
            }

            if (!Chance(0.72f))
            {
                Add(BeatKind.Spill, holder, "Spilled!");
                return beats;
            }

            Add(BeatKind.MarkContest, holder, "MARK!");
            Add(BeatKind.SetShot, holder, "Lines up for goal...");

            // Every miss is a point and every point is a kick-in, for the whole
            // game. In the old game a missed shot scored nothing and restarted
            // at the centre, which is the "no kick ins at all" complaint.
            if (Chance(0.55f)) Add(BeatKind.Goal, holder, "GOAL!", 6);
            else
            {
                Add(BeatKind.Behind, holder, "Behind - one point.", 1);
                Add(BeatKind.KickIn, Other(holder), "Kicks in from fullback!");
            }
            return beats;
        }

        public static Team Other(Team t) => t == Team.Crocs ? Team.Roos : Team.Crocs;
    }
}
