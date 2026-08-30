using Unity.Cinemachine;
using UnityEngine;

namespace AFL.Day1
{
    // =======================================================================
    //  THE CAMERA RIG — Cinemachine 3.1.7 (2026-08-31)
    // =======================================================================
    // Day1RuckContest used to write Camera.main's transform directly, five
    // hard snaps per passage: centre bounce, the kick, the kick-out, the mark
    // close-up and back again. Every one of them was a cut with nothing in
    // between, so the shot changed between one frame and the next.
    //
    // The framing itself was playtested and signed off, so NONE of the numbers
    // change here — the centre shot, kickCamSide/kickCamHeight, the 1.6x
    // kick-out pullback and the subject + (side*7, 3, 0) mark close-up are the
    // same values Day1RuckContest already used. What changes is who applies
    // them: each shot is now a CinemachineCamera, and the Brain blends between
    // them instead of teleporting.
    //
    // The one genuine behaviour change is the close-up. It was a snap to
    // wherever the forward stood at markDeadline, fixed for the rest of the
    // beat; it is now a real Follow with damping, so it stays on the leap as
    // the forward rises instead of framing where they were when it fired.
    //
    // NOTHING HERE MOVES Main Camera. That transform belongs to the Brain
    // alone — a second writer on it is the bug this whole rig exists to
    // prevent. Only vcam transforms are set, which is what a vcam is for.
    //
    // API notes (3.1.7, taken from the working rig in ndw-footy rather than a
    // 2.x sample — these names all differ from 2.x):
    //   using Unity.Cinemachine     not using Cinemachine
    //   CinemachineCamera           not CinemachineVirtualCamera (deprecated)
    //   CinemachineFollow           the Body stage, its own component
    //   CinemachineRotationComposer the Aim stage, its own component
    public class AFLDay1Cameras : MonoBehaviour
    {
        [Header("Shots")]
        [Tooltip("Centre bounce and the default resting shot.")]
        public CinemachineCamera centre;
        [Tooltip("Side-on, placed per beat — the kick and the kick-out both use this.")]
        public CinemachineCamera kick;
        [Tooltip("Tracks a subject through the mark. Follow target is set per beat.")]
        public CinemachineCamera closeup;

        public const int LivePriority = 20;
        public const int IdlePriority = 5;

        CinemachineFollow _closeupFollow;

        void Awake()
        {
            if (closeup) _closeupFollow = closeup.GetComponent<CinemachineFollow>();
        }

        void Live(CinemachineCamera live)
        {
            foreach (var c in new[] { centre, kick, closeup })
                if (c) c.Priority = c == live ? LivePriority : IdlePriority;
        }

        /// <summary>Back to the centre shot. Every round starts here.</summary>
        public void CutToDefault() { Live(centre); }

        /// <summary>Side-on for a kick, pivoted at pivotZ. Static, as before —
        /// the flight reads better against a still frame than a panning one.</summary>
        public void CutForKick(float pivotZ, float side, float height)
        {
            if (!kick) return;
            kick.transform.position = new Vector3(side, height, pivotZ);
            kick.transform.LookAt(new Vector3(0f, 3f, pivotZ));
            Live(kick);
        }

        /// <summary>Tight on whoever is taking the mark. `side` is +1/-1 for
        /// which side of them to sit on — the kick-out needs the mirror, see
        /// Day1RuckContest's own note on why the two are kept separate.</summary>
        public void CutToSubject(Transform subject, float side)
        {
            if (!closeup || !subject) return;
            if (_closeupFollow == null) _closeupFollow = closeup.GetComponent<CinemachineFollow>();
            if (_closeupFollow != null)
                _closeupFollow.FollowOffset = new Vector3(side * 7f, 3f, 0f);
            closeup.Follow = subject;
            closeup.LookAt = subject;
            Live(closeup);
        }
    }
}
