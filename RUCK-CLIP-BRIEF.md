# Brief: replace the ruck's hand-posed hop with a real AnimationClip

Written 2026-08-29. **Verified against the repo before committing — four claims
in the original were wrong and are corrected below.**

## Scope

The ruck leap and ONE Cinemachine vcam. Nothing else. Not the spoil, not the
speccy, not the kick. Shaun plays it and confirms the tap still meets the ball,
then the next commit.

## Corrections to the original brief

Both authors verified these independently in separate trees.

| original said | actually |
|---|---|
| "Cinemachine 3.1.7 is already in Packages/manifest.json" | **NOT INSTALLED** — absent from `manifest.json` and `packages-lock.json`. See "Installing Cinemachine" below; it is its own commit, not a free note. |
| "ArmAngleCheck.cs already does this measurement — use it" | **Does not exist.** The name comes from stale comments in `Day1RuckContest.cs` that cite a tool since deleted. `CheckHandBone.cs` is NOT a substitute — read it: it loads three glbs, reports whether a bone named `RightHand` exists, prints hand-like bone names if not. Zero measurement. |
| line numbers | **Revision-specific and already stale.** Name symbols, never lines. |
| three routines hand-pose | **five** `animator.enabled = false` sites |

### Why the line numbers disagreed

Committed `approved-plus-controls` is 2229 lines with `HopRoutine` at 1941. A
working tree carrying the uncommitted mark-tiers edit is 2262 lines with it at
1974 — a 33-line offset from one uncommitted change. Both readings were correct
for their own tree. That is the whole argument for naming symbols.

## What is actually there

`Day1RuckContest.cs:1974 HopRoutine(Transform t, bool reachesBall)` sets
`animator.enabled = false`, drives `t.localPosition` on a `Mathf.Sin(f * PI)`
wave, and rotates LeftArm/RightArm by hand. That is why the rucks read as
puppets. `SpoilPunchRoutine` (1942) and `SpeccyLeap` (1625) do the same — do the
ruck first, alone.

## The clip

Template: `ndw-basketball-3d/Assets/Editor/MainBuildScript.cs:899 BuildDunkClip()`.
Real `AnimationClip` at 30fps, euler curves on the rig, `loopTime = false`,
saved to `Assets/_Generated/`.

Apply `PreserveOtherAxes` on every bone touched. It exists because of a real
bug found in ndw-unicorn-surf-3d: Unity resets an un-curved euler axis to 0 once
any other axis on that Transform is curve-driven. Skip it and bones twist
off-axis.

**Verify the bone hierarchy before writing a curve.** Basketball's paths are
`Armature/Hips/Spine02/Spine01/Spine/LeftShoulder/LeftArm`. AFL only ever calls
`FindDeepChild(t, "LeftArm")`, so the NAMES match but the PATH is unproven.
Print the real hierarchy from a Croc/Roo glb first. Note the quirk: the spine
runs `Hips -> Spine02 -> Spine01 -> Spine`, the reverse of how it reads.

## THE TIMING — this is what breaks the game if it is wrong

The ruck contest is a timing mechanic. `hopDuration = 0.45f`, and the peak is
`sin(f*PI)` at `f = 0.5` — the jump peaks at exactly 50% of its length, and the
ball is frozen at height 3.1 waiting to meet it. Three places depend on that:

    Day1RuckContest.cs:390    WaitForSeconds(hopDuration / 2f + 0.15f)
    Day1RuckContest.cs:985    WaitForSeconds(hopDuration * 0.5f)
    HopRoutine itself

**`BuildDunkClip` peaks at 0.35, not 0.5.** Copy its keyframe times unchanged and
every one of those waits is 0.07s out, and the tap stops meeting the ball.

The clip must be `hopDuration` long and peak at normalised 0.5. Better: derive
both from `hopDuration` rather than hardcoding, so the peak time is ONE fact,
not two. This repo's documented failure mode is one fact in two places drifting;
the peak time is that fact here.

## The vertical travel does NOT go in the clip

`HopRoutine` moves the ROOT: `t.localPosition = start + Vector3.up * wave * heightScale`.
A clip on `Armature/Hips` and below animates BONES, not the root transform.

So: the clip does the body shape (arms reaching, knees tucking, torso
extending); the coroutine keeps driving root height exactly as it does now. Put
the rise into the clip and delete that line and you get a character pumping its
arms at ground level. Expect to get this wrong once; check for it.

Keeping height in the coroutine also gives the winner/loser difference free:
`heightScale = reachesBall ? contestLeapHeight : 0.5f` already makes the winner
rise 1.65 and the loser 0.5. **Same clip, two heights. Do not build two clips.**

## Do not touch contestLeapHeight = 1.65f or contestArmAngle = 155f

The comment above `SpoilPunchRoutine` records the measurement: arms raised 140°
put hands at world y≈1.48; the ball peaks at `groundY + peakHeight = 1.0 + 2.1 =
3.1`; a 1.5-unit hop brings hands to ≈2.98 — genuine contact. Those came from
rendering and measuring, not guessing. **If the hand stops meeting the ball
after this change, the clip is wrong, not those numbers.**

## Installing Cinemachine — do this as its own commit, first

It is not in this project. Add `com.unity.cinemachine` and **pin 3.1.7**.

**Do not accept 3.1.4** — it fails to compile on Unity 6:
`CinemachineStoryboard.cs:204`, three `CS0619`s. Install, confirm a clean
compile, and commit that alone before writing any camera code. Debugging a
version fight and a new clip in the same commit is how a day disappears.

`link.xml` / `[Preserve]` matters from that commit onward, not before.

## The camera

Construction, from `ndw-basketball-3d/Assets/Editor/MainBuildScript.cs:216-232`:

    var brain = camGO.AddComponent<CinemachineBrain>();
    var vcamGO = new GameObject("RuckVCam");
    var vcam = vcamGO.AddComponent<CinemachineCamera>();
    vcam.Lens.FieldOfView = 60f;
    var cmFollow = vcamGO.AddComponent<CinemachineFollow>();
    cmFollow.FollowOffset = new Vector3(0f, 1.2f, -6f);
    vcamGO.AddComponent<CinemachineRotationComposer>();

`Follow` and `LookAt` go to **the ball**, not to either ruck — the ball is what
both players contest and it never leaves frame.

**One vcam. Not five, not one per beat.** Leave the four existing
`CutCameraFor*` functions alone for now; they serve later beats, and two of the
six bugs on 21 Aug came from their pivot formulas — which is exactly what
Cinemachine removes — but that is a later commit.

Steal `DunkCinemachineCam.cs`'s airborne trick: raise
`follow.TrackerSettings.PositionDamping.y` while the ruck is airborne so the
camera does not snap upward with the jump. That is what makes basketball's leaps
read as high.

Cinemachine must be `[Preserve]`d or in `link.xml` in the same commit — WebGL
stripping already killed `SendMessage` in this project once.

## How to check before claiming it works

The ball sits frozen at 3.1. Print the hand bone's **world Y at the clip's peak
frame**. If it is not ~2.95-3.0 the tap will not read, whatever a screenshot
looks like.

`ArmAngleCheck.cs` does NOT exist — **this measurement tool has to be written.**
Instantiate the rig, sample the clip at its peak frame, print the hand bone's
world Y.

`CheckHandBone.cs` is the template for the instantiate-and-find-bone part ONLY.
It is also still the right tool for the separate "verify the bone hierarchy
before writing curves" step above — just not for this one.

Do not substitute "no console errors" or "the render looks fine" for the
measurement.

## Related

`ndw-footy` already solves this differently: real Meshy clips, Cinemachine, no
hand-posing anywhere. Worth deciding whether this retrofit is wanted at all, or
whether the ruck lives in that project instead. Doing both is duplicated work.
