# Brief: chaining a contest's outcome into the next beat

Covers **both** outcomes of a mid-ground contest — the spoil and the mark. They chain through the same routine and must be built together, not one now and one later.

**For the next Claude working on `Assets/Scripts/Day1RuckContest.cs`.**

## Read this first — it corrects `SECOND-CONTEST-BRIEF.md`

That brief said: at chain depth ≥ 1, a spoil ends the round on the placeholder-ending rule, no further chaining. **That is superseded.** Shaun's spec (2026-08-21):

> after some spoils another character gets the ball goes for a run and kicks the ball into the forward line exactly the same as what would happen in the centre

So a spoil is a **continuation**, not a terminator. If the second contest has already been built with a hard stop there, that stop is what changes — do not build a second, parallel path around it.

The recursion guard itself still stands. It changes from *forbidding* the chain to *bounding* it. See below.

## What this is

Same routine again. `TapBallAway(crocWins, kickerOverride, reverseDirection)` already is "a character receives the ball, runs straight, and kicks it downfield" — `RunStraight(rover, runDir)` followed by `KickAway`. That is precisely the described beat. The centre uses it; the clearance path already reuses it via `kickerOverride`, which exists for this purpose.

**Do not write a new run-and-kick routine.** If the clearer's kick needs different code from the rover's kick, something is wrong with the arguments, not with `TapBallAway`.

The existing `else` branch (the uncontested drop, `"Cleared away!"`) already does half of it: it picks a `clearer`, runs them with `RunToZ`, and ends in `MarkCatchRoutine`. It stops there — no kick. That branch and this one should converge on the same handler rather than growing two similar-looking clearance paths side by side; two near-identical paths that drift apart is this project's documented failure mode.

## The mark outcome — and the one genuinely new decision

Shaun, same conversation:

> if they take the mark same thing as the start they kick the ball towards the forward

So a mark chains too, via the same `TapBallAway(kickerOverride: theMarker, ...)` call. Both outcomes converge on one routine — that is the design, and it is worth preserving deliberately rather than letting a mark path and a spoil path grow separately.

**But `markedResult` currently goes straight to `TakeShotAtGoal`.** That is right at the forward end and wrong in the middle of the ground — a mark at z≈8 must not produce a set shot from 60 metres out. This is the one thing in the chain that is not just an existing routine called with new arguments:

```
mark taken:
    within scoring range?  -> TakeShotAtGoal  (unchanged, ends the round)
    otherwise              -> TapBallAway(kickerOverride: marker, ...)  -> next contest
```

**Scoring range is a new number, and it is exactly the trap `CLAUDE.md` warns about.** Do not pick it by eye. It has to agree with values that already exist — `kickDistance` (how far a kick actually travels) and `goalZ` (where the goal is). A range the ball cannot physically reach, or one that triggers a shot from further than a kick carries, is two facts written in two places drifting apart in the usual way. Derive it:

```csharp
// A shot is only offered from somewhere a kick can actually reach the goal.
// Derived from kickDistance rather than typed in, so tuning kick power can
// never silently leave this behind — the recurring failure in this project.
float shotRangeZ = goalZ - kickDistance;
```

Then check `Mathf.Abs(ball.position.z)` against it. If `kickDistance` is later tuned, this follows automatically.

## Where the chain actually ends

With both outcomes chaining, the depth cap is no longer the main terminator — and it should not be. The natural ending is football's own: the chain moves the ball downfield, and once a mark is taken **in range**, it becomes a shot at goal and the round finishes.

That is the arc to build for. The cap is a safety net for the case where play keeps spoiling and never reaches the forward line — not the expected way a round ends. If most rounds are ending on the cap rather than on a shot, the chain isn't advancing the ball downfield and that is the bug, not the cap.

## Who the clearer is

The existing branch uses `Transform clearer = humanControlled ? rooClearer : crocClearer;` — the *defending* team's clearer. Confirm that's still right after a spoil in the kick-out contest, where the notion of which team is defending has already flipped once. Trace which team actually has possession at that point rather than copying the line.

## The bound

The chain is now: contest → spoil → clearer → kick → contest → spoil → clearer → … with no natural end. Real football, but it cannot ship unbounded.

Keep the `chainDepth` parameter. Instead of stopping the chain at depth ≥ 1, allow it up to a fixed cap:

```csharp
// Shaun 2026-08-21: a spoil hands off to a clearer who runs and kicks into
// the forward line — the chain continues rather than ending. Capped because
// contest -> spoil -> clearer -> contest has no natural terminus.
public int maxChainDepth = 3;
```

At the cap, end the round on the placeholder-ending rule — reset and an honest message, not a stall into nothing.

Pick the cap by watching it. Three is a starting guess, not a measured value; the real constraint is how long a child will sit through one round without touching a control. If a capped round runs long, the cap is too high regardless of what the code does.

## What must be passed in, not assumed

Same four as the second contest — all of them recur here, because every one is derived from a global that is only correct at the centre bounce:

1. **Contest anchor** — `peakZ` derives from `rover.position.z`. With a `kickerOverride`, that is the clearer's position, which may be nowhere near the ball. The ball's actual position is the anchor.
2. **Camera pivot** — `CutCameraForKick`'s `contestZ` parameter (added in the second-contest work) must be passed here too. Default `goalZ - 5f` is wrong for every contest that isn't the centre one.
3. **Player placement** — `forward`/`defender` are wherever the previous beat left them.
4. **Direction** — `runDir` must point toward the forward line the clearer is kicking into. After two direction flips in one round this is easy to get backwards; verify it rather than reasoning it out.

## Mechanics stay identical

Unchanged from the second-contest brief and worth repeating because the pressure to violate it grows with each chained beat: **do not re-tune jump timing.** `markPerfectWindow`, `markReactionCompensation`, `speccyChance`, `minMarkHoldDuration` are the same numbers in every contest in the game. A contest that feels wrong is a positioning or framing defect. Re-tuning to fix the feel of the third contest silently breaks the first one, which is already signed off.

`CLAUDE.md`'s control rule applies too: every beat has the *same* number of controls. If this hand-off seems to need a new button, the hand-off is wrong.

## Definition of done

Four claims, only the last counts — state explicitly which applies.

A human plays it, and across enough runs to see a spoil chain at least twice:

- whoever is kicking visibly **has the ball** before running — the kick-out's defect was the ball never being attached to the kicker, and both of these beats have the same shape
- the run and kick read the same as the centre's, from a mark and from a spoil alike
- the ball lands in the forward line where a contest is actually set up
- both players are in frame for each chained contest, at whichever end it happens
- a mark **out of range** kicks to the forward; a mark **in range** takes the shot — and the boundary between them doesn't produce a visibly absurd set shot from distance
- most rounds end on a **shot at goal**, not on the cap
- the round always ends somehow — no stall, no silent loop
- total round length is still watchable for a child

## If it goes wrong

Print `ball.position`, `clearer.position`, `peakZ`, `runDir`, `chainDepth` and the camera pivot at each hand-off, and read them. Every defect in this sequence so far has been visible in the coordinates and invisible in the symptom description.
