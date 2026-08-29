# How to build this game — start here

Written 29 Aug 2026 for whoever picks this up next. Everything below was checked
against the code and the live bucket, not copied from another document. Two days
were lost to documents that were confidently wrong; if this one conflicts with
what you observe, **believe what you observe** and fix this file.

---

## 1. Where you are

    live build     gs://ndw-game-builds/afl3d/     stamp v=4de2621f38
    live branch    approved-plus-controls          37fc761
    live script    Day1RuckContest.cs, 2229 lines

**Work on `approved-plus-controls`.** Do not work on `main` — it carries 73
commits Shaun never played, 37 of which are undos.

**Never state what is live from a document.** One command settles it:

    curl -s https://storage.googleapis.com/ndw-game-builds/afl3d/index.html | grep -o 'v=[a-z0-9]*'

There are ten branches with confusable names. If the stamp does not match what
you think is live, stop and find out why before touching anything.

### Archived original builds — real bytes, not rebuilds

    afl3d-21aug             31,102,400   the 21/22 Aug build Shaun signed off
    afl3d-prescoreboard     31,101,060   before scoreboard/quarters/finals
    afl3d-fullground        31,100,967   last build with maxChainDepth = 3
    afl3d-19aug-original    31,098,412   19 Aug

`https://storage.googleapis.com/ndw-game-builds/<slot>/index.html`

**`afl3d-19aug` does not exist — it 404s.** The 19 Aug build is the
`-original` slot. Do not report the build as lost.

These have no `?v=` stamp because they are byte-exact restores. A browser that
has loaded one will keep replaying it — compare them in separate private
windows or you will be looking at whichever you opened first.

---

## 2. The one rule that matters

**One change. One build. Shaun plays it. Then the next.**

This is written in five documents and was ignored every time — 73 commits in one
day, then 14 in 67 minutes. Every serious defect in this project entered during
one of those runs.

There is now a commit gate (`.claude/hooks/deploy-gate.sh` on the
`spilled-removed` branch — copy `.claude/` across) that blocks `git commit` once
you are more than 2 commits past the last deploy. Clear it with
`.claude/hooks/mark-deploy.sh`, and **only after a build has actually reached
him**. Moving the marker on a push defeats the point.

If you have made two commits without a playtest between them, stop and deploy.

### When he says it looks wrong, it is wrong

He can see the screen. You cannot. His report outranks any conclusion you draw
from the code, including your own change that you are sure is correct. Find the
cause; never dispute the observation. If your reasoning says he should be happy
and he is not, your reasoning is wrong somewhere you have not looked.

---

## 3. The bug family — check for it before you write anything

Every significant defect in this project has been one of two shapes: **a value
computed and never read**, or **a constant borrowed from a different beat.**

    _bestHumanErr      never read        ruck unwinnable
    markBestErr        never read        mark timing ignored  (STILL LIVE, see §5)
    shotKickHeight     borrowed          spoil flew like a set shot
    hardcoded 1.4f     vs derived 1.65   players never met in the air
    Mathf.Abs on range dropped sign      shots taken from own defence
    scene in git       regenerated       archaeology measured the wrong file
    stripping setting  survived a revert tap button died silently

**Two habits prevent all of it.** Grep for *reads*, not assignments — a variable
assigned everywhere and read nowhere is the bug. And give every tunable its own
named field; never reuse another beat's constant, however similar.

---

## 4. Traps that will cost you hours

**The committed scene is decorative.** `MainBuildScript.PerformWebGLBuild` calls
`BuildSceneContents(saveToDisk: true)`, which opens with
`EditorSceneManager.NewScene(EmptyScene)` and overwrites
`Assets/Scenes/AflMatch.unity`. Every build discards the committed scene and
rebuilds it from code. So **rebuilding a commit does not reproduce its build**
(archived 22 Aug = 31,102,400; a rebuild of the same commit = 31,120,864), the
GCS artefacts are the real record, and the endless scene YAML churn in commits
is the build rewriting it, not anyone editing it.

**Never raise `managedStrippingLevel` without `[Preserve]` or `link.xml` in the
same commit.** All input arrives via
`unityInstance.SendMessage('TouchBridge', 'TapPressed', '')`, resolved by name at
runtime. `TapPressed` and `AFLTouchBridge`'s methods have no static C# caller, so
High stripping deletes them and the tap button goes dead with no error, no
console message, and a clean build. This happened on 28 Aug and cost most of a day.

**Deploy only with `sync-to-gcs.sh`.** Unity caches `.data`/`.wasm` in IndexedDB
keyed by URL; `Cache-Control` does not reach it. A bare `gsutil cp` strips the
`?v=` stamp and the build is invisible to anyone who has played before. Every
rebuild between 17 and 28 Aug was invisible for this reason — which is why
Shaun's memory of which build worked did not match the commits. The script is on
`main` and needs copying to whatever branch you build from.

**Do not set `maxChainDepth` above 1 without asking.** Killed twice by his own
playtests ("all over the shop").

**Do not use a Canvas for HUD.** From `ndw-unicorn-surf-3d`: a Canvas/Text setup
can be enabled, positioned, coloured — every inspectable value correct — and
still never render in this project family's deployed WebGL. **OnGUI is the
proven path** and is what this game already uses.

---

## 5. The state of the game, and the next change

The live build is **one button, timing-based, no power meter.** Shaun tried a
power bar and dropped it ("my power bar idea was getting to complicated"). The
button relabels itself per beat via `TapLabel()` — TAP, RUN, MARK, GOAL.

**The single next change, and it is small:**

`markBestErr` is computed every frame against `markTargetT` (declared 723,
assigned 818 and 824) and **read nowhere**. On the live build your kid's timing
on a mark counts for nothing. Meanwhile the speccy is still a coin flip:

    bool isSpeccy = Random.value < speccyChance;   // line 567

Grade the number that already exists:

    very close to markTargetT   ->  SPECCY
    inside markPerfectWindow    ->  normal mark
    outside                     ->  spilled

Delete `speccyChance`. One variable starts being read, the speccy becomes
something a child goes for, and nothing new appears on screen.

Ask him first whether he wants three tiers or just mark-vs-spilled.

### After that, in his own words, in rough order

- **Ground contest** — two players race for a loose ball, winner gathers and
  kicks. He asked for it directly. It is also the safest thing in the game to
  build: two bodies, on the ground, in straight lines. Everything that broke on
  28 Aug was two characters in midair having to agree within a tenth of a second.
- **Run** — the run has *zero* input in it today, so it is a cutscene. Clear →
  clean kick; caught → **shank** to ground → ground contest. No tackle, no held
  ball. The shank already exists in `9368bb2` and is a port, not new work.
- **Kick-out after every behind** — he asked for this explicitly.
- **The ruck should not decide the round.** `bool crocWins` currently hands over
  the entire passage from one tap. Make it decide the *quality* — clean tap, or
  ball to ground and a ground contest — so losing it does not mean watching.

---

## 6. Steal from the sibling games — they already solved this

Same Unity version, same character library, same deploy pattern.

**Characters look athletic in basketball because it plays animations. AFL
puppeteers bones.**

    hand-posed bone manipulation    basketball 2      AFL 52
    turns the Animator OFF          basketball 0      AFL 5

`ndw-basketball-3d/Assets/Scripts/DunkCharacter.cs` fires
`animator.SetTrigger(...)` and a real clip plays. AFL computes poses with sine
waves and disables Mecanim so it does not overwrite them. That is the whole
difference, and it is why the AFL contests took eight commits and still read
wrong.

**Cinemachine works in both siblings and only AFL lacks it:**

    ndw-basketball-3d      3.1.7   DunkCinemachineCam.cs
    ndw-unicorn-surf-3d    3.1.7   CameraManager.cs
    ndw-afl-3d             none    added 28 Aug 10:20, removed the same day

Unicorn Surf's `CameraManager.cs` is the model: **one** vcam whose parameters
change by state, eased with `SmoothDamp`, rather than several rigs. AFL has five
`CutCameraX` methods hand-computing pivots, and four of its bugs were exactly
that arithmetic being wrong. Cinemachine deletes the class — you say *follow
this, look at that*, and there is no pivot to get wrong.

Both were built against `UNITY-CAMERA-GUIDE.md` in the `neurodinoworld` repo.
Read it before any camera work.

**Video already works in a deployed WebGL build of this family** —
`ndw-basketball-3d/Assets/Scripts/CelebrationVideoPlayer.cs`. Shaun has Veo
credits and wants a clip for the speccy. The one technical unknown is already
solved next door. Stream it from the bucket; do not bundle it (the game is
already a 48MB download).

A caution from basketball worth internalising: it contains a confident comment
saying Unicorn Surf "never had the package". True when written on 25 Aug, false
now. **Never describe another repo's state from a comment** — read its manifest.

---

## 7. Definition of done

Not when it builds. Not when it deploys. Not when a screenshot pass has seen it.
**It is done when Shaun plays it and the thing you claim to have fixed reads
correctly to him.**

Say which of the four you mean, every time. Conflating them is how a broken
build shipped live.

---

## 8. How to use Cinemachine here

Shaun asked for this directly. Both siblings run **3.1.7** on this Unity version,
so it is proven — but AFL has three specifics that make it different from a
normal Unity project. Get these wrong and it costs a day.

### The three AFL-specific rules

**1. Install and `[Preserve]` in the SAME commit.** Adding the package roughly
doubles the wasm. The last attempt fixed that by raising
`managedStrippingLevel` to High — which deleted `TapPressed` and killed the tap
button, silently, with a clean build. Never separate these two changes.

    Assets/link.xml   (or [UnityEngine.Scripting.Preserve] on the three methods)

Protect `Day1RuckContest.TapPressed`, `AFLTouchBridge.SetMoveHeld` and
`AFLTouchBridge.MarkPressed`. Nothing calls them from C#; JS calls them by name.

    Pin 3.1.7 exactly. 3.1.4 does NOT compile on Unity 6000.5.7f1 —
    CinemachineStoryboard.cs:204 fails with three CS0619s inside the
    package's own source. Every 3.x release advertises 2022.3 as its
    minimum, which is misleading.

**2. Build the rig in code, not the scene.** `BuildSceneContents` regenerates
`AflMatch.unity` on every build, so anything you wire in the editor is thrown
away. The camera must be constructed in `MainBuildScript.cs`. Basketball does
exactly this — copy the shape:

```csharp
var brain  = camGO.AddComponent<Unity.Cinemachine.CinemachineBrain>();

var vcamGO = new GameObject("MatchVCam");
var vcam   = vcamGO.AddComponent<Unity.Cinemachine.CinemachineCamera>();
vcam.Lens.FieldOfView = 60f;
var cmFollow = vcamGO.AddComponent<Unity.Cinemachine.CinemachineFollow>();
cmFollow.FollowOffset = new Vector3(0f, 0.5f, -4f);
vcamGO.AddComponent<Unity.Cinemachine.CinemachineRotationComposer>();
vcamGO.AddComponent<Unity.Cinemachine.CinemachineImpulseSource>();
```

`CinemachineCamera`, **not** the deprecated `CinemachineVirtualCamera`, per
`UNITY-CAMERA-GUIDE.md`. Use `CinemachineFollow` + `CinemachineRotationComposer`,
not `CinemachineThirdPersonFollow` — the shoulder-offset rig is for an
over-the-shoulder humanoid view, not this game's side-on chase.

**3. ONE camera, not five.** This is the whole point. AFL currently has five
`CutCameraX` methods each hand-computing a pivot, and four of its bugs were that
arithmetic being wrong — the kick-out off-camera, the pivot 8 units short, the
camera inside the post cluster, the camera at the sky. Do not port five rigs
across.

Follow `ndw-unicorn-surf-3d/Assets/Scripts/CameraManager.cs`: **one** vcam whose
parameters change by state, eased with `SmoothDamp` — `FollowOffset` for
pull-back, `Lens.FieldOfView`, `Lookahead.Time`, `TrackerSettings.PositionDamping`.
Its own comment is explicit that multiple vcams are only worth it for a
genuinely different framing, not a parameter tweak. Most of AFL's five are
parameter tweaks.

The one place a second vcam is justified is the mark close-up — that is a real
change of framing.

### What it buys you, concretely

- **The pivot bugs stop existing.** `Follow` + `RotationComposer` means the
  camera tracks the ball. There is no `pivotZ` to compute, so none of it can be
  8 units short.
- **`CinemachineImpulseSource` gives screen shake in one call** —
  `GenerateImpulseWithForce(f)`. A spoil or a big mark landing wants it.
- **Damping replaces hand-rolled smoothing.** `TrackerSettings.PositionDamping`
  is Cinemachine's own version of the `SmoothDamp` time constants; the
  composer's dead zone is its version of "don't fight small target jitter".

### The trap Unicorn Surf already hit, which AFL will hit too

From `CameraManager.cs`, 25 Aug, Shaun: *the jump wasn't visible at all.*

> `FollowOffset`'s Y component tracks the target 1:1 by design, so without this
> the camera rises by the exact amount of the jump — perfectly cancelling it out
> on screen.

Fixed by **multiplying vertical `PositionDamping` while airborne** so the camera
lags the rise — loosen, don't disable. AFL is full of leaps; you will hit this
on the first mark contest. Expect it rather than rediscovering it.

### Order of work

Do the camera **as its own build, with nothing else in it.** Cameras are pure
feel — landing one alongside a gameplay change means Shaun cannot tell which he
is reacting to, which is exactly how the last two days went.

1. Package + `link.xml` + `[Preserve]`, no camera changes. Build. Confirm the
   tap button still works. That single check is the whole risk.
2. One vcam following the ball, replacing `CutCameraToDefault` only. Build, play.
3. Fold the flight cameras into vcam parameters, one at a time.
4. Mark close-up as a second vcam, last.

Read `neurodinoworld/UNITY-CAMERA-GUIDE.md` before step 1 — both siblings were
built from it, and its Airtime Comfort section is the damping rule above.
