# Rebuild plan — how to stop this game generating the same bug forever

Written 28 Aug 2026 for Shaun, and for whichever Claude picks this up. He asked
whether the game should be rebuilt from scratch "as this looks like a game that
will continue having bugs."

He is right that it will. He is right about why. But a from-scratch rewrite is
the wrong shape of fix, and this file argues for a different one.

## Read this first: the committed scene is decorative

Found from the Mac side on 28 Aug, verified here, and it is the most important
structural fact in the project. `MainBuildScript.PerformWebGLBuild` calls
`BuildSceneContents(saveToDisk: true)`, and that method opens with:

```csharp
var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
...
EditorSceneManager.SaveScene(scene, "Assets/Scenes/AflMatch.unity");
```

Every build **throws the committed `AflMatch.unity` away**, rebuilds the scene
from code against whatever assets happen to be present, and overwrites the file
in git. Three consequences, all of which bit someone today:

1. **Rebuilding a commit does not reproduce that commit's build.** Measured: the
   archived 22 Aug build has `WebGL.data` = 31,102,400; rebuilt from the same
   commit it is 31,120,864. Different artefact, same source.
2. **The builds in GCS are the real record, not the commits.** Source-level
   archaeology — including a lot of mine on 28 Aug, comparing scene object and
   component censuses across commits — was chasing a file that has no bearing
   on what ships.
3. **The endless `AflMatch.unity` YAML churn in every commit is the build
   rewriting it**, not anyone editing it. It is noise, and it has been hiding
   real diffs in it for weeks.

This is also the largest instance of the pattern the next section is about: the
scene exists in two places — committed, and regenerated at build — and only one
of them is real. Any rebuild should either make the scene genuinely
source-of-truth (stop regenerating it) or stop committing it at all. Keeping
both is what produces this.

## Every bug this project has had is the same bug

Not a figure of speech. Going through the real history, here is the actual list:

| bug | the two facts that drifted |
|---|---|
| ball floating at goal-post height | ball position vs. the kicker's position |
| kick-out flew off-camera | `CutCameraForKick`'s pivot vs. the traverse it was reused for |
| camera aimed 8 units short | `rover.position.z` passed where `peakZ` was meant |
| "all over the shop" direction | `reverseDirection` vs. the `crocsInPossession` flip — they cancelled |
| players inside one another | `rooClearer` and `rooForward` both at `x = 0.9` |
| normal mark needs no timing | speccy's "never spill" rule applied to both paths |
| taps stopped registering | stripping raised for Cinemachine, outlived Cinemachine's revert |
| the 15:42 revert overshot | `Assets/` restored, `ProjectSettings/` not |

`CLAUDE.md` already names this at its top: *one fact about the world written
down in two places that then drift apart.* That diagnosis is correct and has
been correct for weeks. What has not happened is changing the code so the
pattern **cannot recur**. Every fix so far has repaired one instance of it.

A from-scratch rewrite in the same style would reproduce the whole table within
a fortnight, because the style is what produces it.

## The second problem: nothing can be checked without playing it

The repo's definition of done is right — builds / deploys / screenshot-verified
/ human-played are four different claims. But right now **human-played is the
only one that can catch a gameplay bug at all**, which makes every bug
expensive and every fix a gamble.

Measured facts about `Day1RuckContest.cs` as it stands:

- **2,388 lines**, one `MonoBehaviour`, 14 coroutines
- **49 tunable `public float`s**
- **13 hard-coded `WaitForSeconds`** — timing baked into control flow
- **5 unseeded `UnityEngine.Random` calls** — so no bug is reproducible
- **0 tests**, no `.asmdef`, no Unity Test Framework
- `AFLGameManager.cs` is **652 lines of dead code** — nothing instantiates it
  (confirmed 19 Aug in `7e4cc61`, still there nine days later)

Those 5 unseeded random calls are the quiet killer. When Shaun says "it's
inconsistent", there is currently no way to reproduce what he saw. Every
diagnosis is inference from a description.

## What to actually do

Not a rewrite. Keep the presentation code — the kick-out's ball-tracked-to-boot
motion, the camera cut timed to the leg snap, the speccy leap. That is the most
heavily tuned code in the project, it took real work, and it is **not** where
the bugs come from. Rebuild the *control flow* around it.

### 1. Seed the randomness (smallest change, biggest return)

One `System.Random` per round, seeded from a value shown on screen and logged.
Then "it did the weird thing" becomes "round 47, seed 8814" and is reproducible
on demand. Do this first — it makes everything below verifiable.

### 2. Split the simulation from the presentation

A round becomes a pure function: `(seed, inputs) -> List<Beat>`, where each
beat carries who has the ball, which way they are going, and what the outcome
roll was. No `Transform`, no `WaitForSeconds`, no camera. The presentation layer
replays that list.

This is the change that makes everything testable, because the sim runs
headlessly in milliseconds.

### 3. Derive, never pass

This kills the entire first table.

- direction comes from the **team** (Croc +Z, Roo -Z), always, everywhere — it
  is never a parameter, so it can never disagree with possession
- ball position comes from **whoever holds it**, not from a copy
- the camera's target comes from **the beat's subject**, not a recomputed pivot

If a value can be derived, it must never be stored or passed. Every row in that
table is a stored copy that drifted from its source.

### 4. Beats as data, not re-entrant coroutines

`maxChainDepth` exists only because `TapBallAway` calls itself and there is no
natural stopping point. A round expressed as a list of beats has a natural end,
so the depth cap — and the two separate corrections it needed on 21 Aug —
stops being a thing that can be got wrong.

### 5. Add an `.asmdef` and the Unity Test Framework

Then these become tests instead of playthroughs:

- a spoil at the goal line always produces a rushed behind, then a kick-out
- a kick-in leaves the ball inside the field bounds, at every chain depth
- the team that was scored on takes the kick-in
- no two players ever occupy the same lane
- a round always terminates

Run them in CI on every push. The pipeline already exists.

### 6. Delete the dead code

652 lines of `AFLGameManager.cs` plus its satellites. Every session reads past
it and some have edited it by mistake — `7e4cc61` records exactly that.

## Cinemachine: yes, and here is the condition

Shaun asked directly. Yes — and for a better reason than "it's nicer".

Four separate camera bugs in the table above are all hand-computed pivot maths
drifting from the value it should have used. Cinemachine replaces *compute a
pivot* with *follow this target, look at that one*, which deletes that bug
family by construction rather than fixing instances of it.

`780bafb` already did the wiring, conservatively and well — each of the five
`CutCameraX` methods wrote its original framing onto a dedicated vcam,
numbers untouched. That commit is reverted but is a good reference; do not
redo the research.

Two things it recorded that are worth keeping:

- **Pin 3.1.7.** Cinemachine 3.1.4 does not compile on Unity 6000.5.7f1 —
  `CinemachineStoryboard.cs:204` fails with three CS0619s inside the package's
  own source. Every 3.x release advertises 2022.3 as its minimum, which is
  misleading.
- **It costs 13.7MB unstripped, 207KB stripped** — but read the next paragraph
  before quoting that number, because it is easy to misapply and I misapplied
  it myself.

**Correction (28 Aug, from the Mac side, and it is right).** That 31.4MB was
measured at `780bafb`, when the scene held **5 live Cinemachine references**.
The current tree has **0** — the `Assets/` reverts took every vcam out — so the
assembly is unreferenced and the linker drops it even at default stripping.
The package as it sits on `main` is inert, and nothing is currently shipping
13.7MB to anyone. The live wasm being 17,636,174 bytes does not prove the point
either way, because that build was made with High stripping still set.

I claimed in `b5fb39a` that rebuilding with `a52a29c` alone would ship ~31MB.
That was not established — it assumed a figure measured with the package in use
still applied once it was unused. Dropping the package is still the right move,
because it makes `Packages/` byte-identical to the last known-good build
instead of a configuration that has never been built and played. But the reason
is tidiness and reproducibility, not a size emergency.

And one thing it recorded that is **wrong**, and caused today's outage:

> "High is safe here specifically because nothing in this project resolves
> types by reflection."

This project resolves a method by reflection **on every tap**. The Day1 WebGL
template drives input through
`unityInstance.SendMessage('TouchBridge', 'TapPressed', '')`, a by-name runtime
lookup, and neither `TapPressed` nor `AFLTouchBridge.SetMoveHeld` /
`MarkPressed` has any static C# caller. High stripping deletes them. The chain
was: package added → size doubled → stripping raised to pay for it → stripping
outlived the package's revert → taps died, and the game looked like it was
playing itself.

**So if Cinemachine comes back, it ships in the same commit as either
`[UnityEngine.Scripting.Preserve]` on those three methods, or a `link.xml`.**
Never re-raise stripping on its own.

Better still, as part of the rebuild: **stop using `SendMessage` for input at
all.** `Day1Input.TapDown` already accepts a direct mouse/touch click
(`edff8a9`, 19 Aug). Finish that job and the most fragile thing in the project
— the one that silently breaks with no compile error and no console message —
stops existing.

## Order of work

1. Seed the RNG. Ship it. Nothing else changes.
2. Delete the dead code. Ship it.
3. `.asmdef` + first three tests against the code as it stands.
4. Extract the sim from the presentation, behind the tests.
5. Derive-never-pass, one value at a time, each with a test.
6. Beats as data.
7. Cinemachine, with `[Preserve]` in the same commit.

One at a time, each played by Shaun before the next. That is not caution for
its own sake — steps 4 and 6 are the ones that can silently change how a round
feels, and feel is the only acceptance criterion this game has.

## What not to do

- **Do not bulk-restore or bulk-revert.** Both of today's reverts were
  bulk operations and both were wrong in the same way: they restored `Assets/`
  and left a setting behind. Revert every directory a session touched, or
  revert nothing.
- **Do not set `maxChainDepth` to 3.** Killed twice by Shaun's own playtests.
- **Do not deploy with a bare `gsutil cp`.** Unity WebGL caches `.data`/`.wasm`
  in IndexedDB keyed by URL; `Cache-Control` does not reach it. Use
  `sync-to-gcs.sh`, which stamps `?v=<hash>` onto index.html. CI does **not**
  do this — it sets no-cache headers only, which is not the same thing.
- **Do not trust a commit message's reasoning without checking it.** The
  stripping outage came from one confident, wrong sentence in an otherwise
  careful commit.

## The honest summary

The game does not need rebuilding. It needs its control flow restructured so
that the one bug it keeps having becomes impossible, and a test harness so that
the second-order problem — nothing is checkable without Shaun playing it —
stops making every fix a gamble.

Steps 1 and 2 are an afternoon and would pay for themselves immediately.
