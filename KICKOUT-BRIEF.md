# Brief: the kick-out sequence — read this before touching it again

**For the next Claude working on `Assets/Scripts/Day1RuckContest.cs`, kick-out beat (the `defenderSpoiled` branch, ~line 750 onward).**

Five consecutive commits (f96f198, 3756099, 115cda5, 1158135, d67d296) have fixed this beat, and it is still wrong. Each of those commits was a *correct* fix to a *correctly reported* symptom — the ball height really was stuck at 3.7, the camera really was pointed at the wrong end, the facing really was arbitrary after a zero-length run. None of them were wrong. They were just all downstream of a fact nobody wrote down.

This is the failure `CLAUDE.md` names at the top: **one fact about the world written in two places that then drift.** Here the twin is *where the ball is* and *where the kicker is*.

---

## The actual defect

Trace the numbers rather than the symptom descriptions:

| what | value | where set |
|---|---|---|
| ball after the behind | `x = 1.6` (hardcoded) | `behindTarget` |
| defender after the slide | `x = ±2.6` | `clearX` |
| ball at kick time | `kickOutStart = ball.position` → still `x = 1.6` | kick-out loop |

`KickMotion(defender, ...)` plays the leg snap at `x = ±2.6`. The ball departs from `x = 1.6`. **Nothing in this beat ever puts the ball at the defender's feet.** The kicker and the ball are two unrelated objects that happen to animate at the same time.

That is the "you need to make sure you see them kick the ball out" note, still unaddressed — no camera change can fix it, because the camera was never the problem.

Note that `MarkCatchRoutine` already solved this exact class of problem the right way, and says so in its own comment: *track the real bone, not a guess.* It snaps the ball to `FindDeepChild(forward, "RightHand")` and keeps tracking it live. The kick-out never got the same treatment.

### Two consequences that make it look intermittent

**The side is a coin flip.** `behindTarget.x` is hardcoded `+1.6`. `clearX` derives its sign from `defender.position.x`, which is the mark-contest spawn (~±0.9 from `MainBuildScript`). So on one spawn side the defender ends 1.0 units from the ball; on the other, 4.2 units away and on the opposite side of the ground. Same code, two very different-looking results — which is why a fix can appear to work and then not.

**The close-up doesn't mirror.** `CutCameraToMarkCloseup` uses a fixed world-space `(7, 3, 0)`. The slide-clear-of-posts fix (d67d296) preserves whichever side the defender was on — but the camera offset was never mirrored to match. At `x = -2.6` the camera sits at `+4.4` and its sightline back to the defender crosses the post cluster (`-1.3..1.3`) that the slide existed to avoid. **The post fix only ever landed on +X.**

---

## The patch

Three changes. Do them together — individually, 1 and 2 each leave a visible artefact.

### 1. One side, derived once

Before the behind kick (the ball reset to `groundY` is already there — keep it), establish the side a single time and use it for both the ball and the defender.

```csharp
// Ball and kicker must end up on the SAME side. Previously behindTarget
// hardcoded +1.6 while clearX followed the defender's spawn sign, so half
// the time they finished 4.2 units apart on opposite sides of the goal.
float side = Mathf.Sign(defender.position.x == 0f ? 1f : defender.position.x);
```

Then:

```diff
- Vector3 behindTarget = new Vector3(1.6f, behindKickStart.y, zDir * goalZ);
+ Vector3 behindTarget = new Vector3(side * 1.6f, behindKickStart.y, zDir * goalZ);
```

```diff
- float clearX = Mathf.Sign(defender.position.x == 0 ? 1f : defender.position.x) * 2.6f;
+ float clearX = side * 2.6f;
```

`side` must be captured **before** the slide, while `defender.position.x` still holds the spawn value.

### 2. Give the defender the ball

Immediately before the `KickMotion` call, snap the ball to the boot and hold it there for the duration of the leg motion, then let the existing arc take over. Same idiom as `MarkCatchRoutine`.

```diff
  _message = "Kicks out from fullback!";
  defender.rotation = Quaternion.Euler(0, zDir > 0 ? 180 : 0, 0);
  CutCameraToMarkCloseup(defender);
  yield return new WaitForSeconds(kickOutPause);
  if (_roundId != roundAtStart) yield break;
- yield return KickMotion(defender, kickMotionDuration);
+ // The ball was landing at behindTarget and never moving again — the leg
+ // snapped at the defender's position while the ball sat a metre-plus away
+ // and then launched itself. Put it on the boot and keep it there through
+ // the kick, same as MarkCatchRoutine does with the hand.
+ var boot = FindDeepChild(defender, "RightFoot");
+ if (ball) ball.position = boot ? boot.position
+                                : defender.position + Vector3.up * (groundY * 0.5f);
+ yield return StartCoroutine(KickMotionWithBall(defender, boot, kickMotionDuration));
  if (_roundId != roundAtStart) yield break;
```

New coroutine, sitting next to `KickMotion` — it does not replace `KickMotion`, which is used elsewhere:

```csharp
// KickMotion, but the ball rides the boot for the duration. Split rather
// than folded into KickMotion because the centre-clearance kick calls that
// one with the ball already in flight and must not have it yanked back.
System.Collections.IEnumerator KickMotionWithBall(Transform t, Transform boot, float duration)
{
    int roundAtStart = _roundId;
    var inner = StartCoroutine(KickMotion(t, duration));
    float el = 0f;
    while (el < duration)
    {
        if (_roundId != roundAtStart) yield break;
        el += Time.deltaTime;
        if (ball && boot) ball.position = boot.position;
        yield return null;
    }
    yield return inner;
}
```

After this, `kickOutStart = ball.position` picks up the boot position for free — leave that line alone.

### 3. Mirror the close-up

**Do not edit `CutCameraToMarkCloseup` itself.** The mark beat uses it and Shaun has already signed that framing off ("really being able to see the person grabbing the mark"); changing shared behaviour here would silently alter a beat that is currently accepted. Add an overload instead:

```csharp
// Mirrored variant. The fixed +X offset means that when the subject is on
// -X the camera ends up INSIDE the post cluster's sightline — the exact
// occlusion the kick-out slide was added to avoid. Kick-out uses this;
// the mark keeps the original.
void CutCameraToMarkCloseup(Transform subject, float side)
{
    if (!_mainCam || !subject) return;
    _mainCam.transform.position = subject.position + new Vector3(side * 7f, 3f, 0f);
    _mainCam.transform.LookAt(subject.position + Vector3.up * 1.2f);
}
```

```diff
- CutCameraToMarkCloseup(defender);
+ CutCameraToMarkCloseup(defender, side);
```

---

## Definition of done for this beat

Per `CLAUDE.md`, none of "it builds", "it loads", or "a screenshot saw it" count here. State which of the four claims applies, explicitly.

A human watches the rushed-behind → kick-out chain **run at least four times**, so both spawn sides come up. Every run, all of:

- the ball is visibly **at the defender's boot** when the leg snaps — not nearby, touching
- the defender is on screen and **not behind a post**, on either side
- the ball's flight is in frame from boot to landing
- the ball does not visibly teleport between the behind landing and the kick

Four runs is the point. A two-run check can land the same spawn side twice and pass a beat that is still broken half the time — which is plausibly how this survived five passes.

## If it is still wrong after this

Do not add a sixth camera fix. Print the actual numbers — `ball.position`, `defender.position`, `boot.position` — at each step of the sequence and read them. Every defect in this beat so far has been visible in the coordinates and invisible in the symptom description.
