# ndw-afl-3d (Mount Duneed Cats Footy)

Unity WebGL game. The built player ships into the NeuroDinoworld app at www/unity-games/afl3d/ and is embedded there as an iframe.

## Before the next pass, read issue #1 and issue #2 — not the old FIX BRIEF

**2026-08-11, superseding everything below this line that references the old FIX BRIEF or says not to rebuild from scratch.** Three rebuilds of the gameplay layer (a659d85, then the Meshy-rig pass, then the v530 beat-system rewrite) all shipped clean builds and all failed the same real playtest: unresponsive control, players in the wrong place for the beat they're in, a contest system nobody — human or bot — could read fairly. That is no longer a "keep patching" situation. Shaun's call, made explicitly: **start from scratch, one capability at a time.**

The current plan is issue #1's pinned comment "The from-scratch rebuild plan — canonical, written down here" (posted 2026-08-11). Read that comment, not the issue body above it — the issue body is the *previous* spec (the 5-goal-chain beat system) and is now historical context, not the active plan. The new plan is five days, each adding one capability to a single persistent scene:

1. Two rucks at the centre — the contest alone, nothing else.
2. Add the rovers — ruck tap hands off to a rover.
3. Run and kick — the rover moves and disposes of the ball.
4. The mark — full spec in issue #2, read it before writing any of this day's code.
5. Shot at goal — connects the chain end to end for the first time.

Every day must be playable start to finish on its own (the "placeholder-ending rule" — end honestly on a reset/message rather than stall into not-yet-built work). Do not build day 2 before day 1 is confirmed playable by an actual human. "It builds" and "it loads" are not that confirmation — see Definition of Done below, unchanged from before.

**Blocking question for day 1, not yet answered**: Shaun referenced a specific example of correctly rigged characters that never reached this repo. Confirm what that is before writing day 1 — do not assume the existing Meshy-rigged Croc/Roo (`Assets/Models/CrocRiggedAI`, `RooRiggedAI`) are what he means without checking, given the rig question has already been reopened twice in this project's history for exactly that kind of assumption.

Also read `neurodinoworld` issues #1 (verification tracking for the v530 shipment), #2 (positions don't match the beat), and #3 (control feels unresponsive) — real bug reports from that evaluation, useful context for what specifically went wrong even though the fix is a rebuild, not a patch.

## The recurring failure in this game

Every version of this game so far, in both the 2D and the Unity rebuilds, has broken in the same way: one fact about the world written down in two places that then drift apart. Player gravity against project gravity. Kick power against field size. Reach against where the model's hands actually are. Before tuning anything, check whether the number you are about to change has a twin somewhere else that also has to move. Issue #2 (the mark spec) restates this for jump timing specifically: the press window the player sees and the value the game grades against must be the literal same number, not two systems that are supposed to agree.

The target player is a child on a touchscreen, with real input latency between the HTML control bar and Unity. Anything that needs sub-100ms timing does not ship — issue #2 makes this concrete: nothing in this game may ever require timing tighter than 0.25s.

## Definition of done

This game is not done when it builds, not when it deploys, and not when the console is clean. None of those checks can detect the things that actually make it unplayable — a clean console has now accompanied four separate unplayable builds in this project's history (see issue #1's evaluation of v530 for the most recent).

It is done when a person plays it for five minutes and, without being coached, reaches the ball on foot, takes at least one mark, gets at least one shot at goal, never loses the ball off the field with no way to restart, and can see the ball and their own player the whole time.

Until a human has actually played it, report the status as not verified, not working. Four different claims exist and only the last one counts: it builds; it loads; a screenshot pass has seen it on screen; a human has played it. Say explicitly which applies, every time.
