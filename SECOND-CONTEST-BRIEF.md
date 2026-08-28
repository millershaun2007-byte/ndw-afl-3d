# Brief: second marking contest after the kick-out

**For the next Claude working on `Assets/Scripts/Day1RuckContest.cs`.**

**Do not start this until the kick-out patch (`KICKOUT-BRIEF.md`) has been applied AND a human has watched four clean runs.** `CLAUDE.md`'s rule is explicit — one capability at a time, previous one confirmed playable by an actual human first. "It builds" is not that confirmation. This beat chains directly onto the kick-out; building it on top of an unverified kick-out means any failure has two possible homes and you will not be able to tell them apart.

---

## What Shaun asked for

After the kick-out, two players contest a mark — same fashion as the contest earlier in the game. **Jump timing and mechanics stay identical.** The camera angle is the part that needs to be got right.

Take that literally. Do not re-tune `markPerfectWindow`, `markReactionCompensation`, `speccyChance`, `minMarkHoldDuration`, or any jump timing for this contest. They are the same numbers. If the second contest feels different from the first, that is a bug in positioning or framing, not a reason to re-tune the grading — and re-tuning would break the contest that already works.

## The good news: the mechanic is already re-entrant

`TapBallAway(bool crocWins, Transform kickerOverride = null, bool reverseDirection = false)` was already parameterised for reuse, and the clearance path already reuses it. The whole contest — `RunStraight`, the speccy roll, `SpeccyLeap`/`NormalMarkHop`, `KickAway`'s grading, `_markHoldReleased` — comes along for free.

So the chain is one call at the end of the kick-out arc, after `ball.position = kickOutTarget`:

```csharp
yield return TapBallAway(crocWins: !humanControlled,
                         kickerOverride: defender,
                         reverseDirection: true);
```

Confirm the `crocWins`/`reverseDirection` combination produces a `runDir` pointing **away from the defending goal** before writing anything else — `runDir = (crocWins ? 1f : -1f) * (reverseDirection ? -1f : 1f)`, and getting this wrong sends the whole contest back into the goal it just came out of.

## The four things that must be passed in, not assumed

This is the project's documented recurring failure — one fact in two places. Every item below is a value the first contest derives from a global that is only correct at the centre bounce.

### 1. The contest anchor

`peakZ = rover.position.z + runDir * kickDistance * 0.5f`. With `kickerOverride: defender`, `rover.position.z` is the goal square (`zDir * goalZ`, z=±20) — but the ball is sitting at `kickOutTarget` (z = `zDir * 8`). The contest zone and the ball would be computed from two different anchors, twelve units apart.

**The ball's landing position is the anchor.** The contest must be built around where the ball actually is, not where the kicker stands.

### 2. The camera pivot

`CutCameraForKick(runDir)` hardcodes `pivotZ = runDir * (goalZ - 5f)` — pinned to the goal. Correct for the centre bounce (the ball is heading goalward); wrong here (the contest is at z≈±8, and heading further out).

Add an optional anchor rather than editing the existing behaviour:

```csharp
// pivotZ was pinned to goalZ, which is only right when the contest is
// heading INTO the goal. The kick-out contest happens mid-ground and
// moving away from it, so the pivot has to follow the contest.
void CutCameraForKick(float zDir, float? contestZ = null)
{
    if (!_mainCam) return;
    float pivotZ = contestZ ?? (zDir * (goalZ - 5f));
    _mainCam.transform.position = new Vector3(kickCamSide, kickCamHeight, pivotZ);
    _mainCam.transform.LookAt(new Vector3(0, 3f, pivotZ));
}
```

Existing callers pass one argument and are unaffected. **Keep `kickCamSide`/`kickCamHeight` unchanged** — do not reach for the 1.6x pull-back from `CutCameraForKickOut`. That widening exists because the kick-out traverses ~20 units in one static shot; this contest is the same size as the centre one, so it wants the same framing. If it doesn't read, the pivot is wrong, not the distance.

### 3. Who contests

`forward`/`defender` come from `crocWins ? crocForward : rooForward`. After the kick-out, those four transforms are wherever the *first* contest and the slide left them — the defender in particular is at `clearX = side * 2.6` on the goal line. They need placing relative to the new contest zone before the contest starts, or they'll run at it from wherever they happen to be standing.

### 4. Sequence-end state

`TapBallAway` sets `_resolvedAt` and `_sequenceComplete = true` at its end. Called from inside `KickAway`, the inner call sets both, then the outer one sets them again on return. Verify by reading `Update()` that no reset can fire in that window — and if the chained call is the true end of the sequence, the outer assignment is the one that's now wrong.

## Recursion guard — required, not optional

The chain is `TapBallAway` → `KickAway` → spoil branch → `TapBallAway`. **If the second contest also spoils, it chains a third, and so on without limit.** Add a depth parameter and stop the chain at the first level:

```csharp
System.Collections.IEnumerator TapBallAway(bool crocWins,
                                           Transform kickerOverride = null,
                                           bool reverseDirection = false,
                                           int chainDepth = 0)
```

At depth ≥ 1, a spoil ends the round on the placeholder-ending rule (reset + message) instead of chaining another kick-out. Per `CLAUDE.md`: end honestly rather than stall into not-yet-built work.

## Definition of done

Four claims exist and only the last counts — say explicitly which applies. A human plays it, and every run:

- both players are **visible and in frame** for the whole contest, at either end of the ground
- the jump reads the same as the centre contest — same timing feel, no sense of a different or unfairer window
- the ball arrives in the contest zone, not twelve units from it
- a spoil at this stage **ends the round cleanly** — no second kick-out, no stall
- the camera never points at the end of the ground the contest isn't at

Run it enough times to see both a mark and a spoil, and both spawn sides. A single run that happens to mark cleanly proves almost nothing here.

## If it feels wrong

The instruction was that the mechanics stay the same. So if the contest feels off, the cause is in positions, framing, or direction — not the grading numbers. Print `ball.position`, `forward.position`, `defender.position`, `peakZ` and the camera pivot at each step and read them before changing a single tuning value.
