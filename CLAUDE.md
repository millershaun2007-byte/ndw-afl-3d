# ndw-afl-3d (Mount Duneed Cats Footy)

Unity WebGL game. The built player ships into the NeuroDinoworld app at www/unity-games/afl3d/ and is embedded there as an iframe.

## Before the next pass, read issue #1, issue #2 and issue #4, plus millershaun2007-byte/ndw-character-library#1 — not the old FIX BRIEF

**2026-08-11, superseding everything below this line that references the old FIX BRIEF or says not to rebuild from scratch.** Three rebuilds of the gameplay layer (a659d85, then the Meshy-rig pass, then the v530 beat-system rewrite) all shipped clean builds and all failed the same real playtest: unresponsive control, players in the wrong place for the beat they're in, a contest system nobody — human or bot — could read fairly. That is no longer a "keep patching" situation. Shaun's call, made explicitly: **start from scratch, one capability at a time.**

The current plan is issue #1's pinned comment "The from-scratch rebuild plan — canonical, written down here" (posted 2026-08-11). Read that comment, not the issue body above it — the issue body is the *previous* spec (the 5-goal-chain beat system) and is now historical context, not the active plan. The new plan is five days, each adding one capability to a single persistent scene:

1. Two rucks at the centre — the contest alone, nothing else.
2. Add the rovers — ruck tap hands off to a rover.
3. Run and kick — the rover moves and disposes of the ball.
4. The mark — full spec in issue #2, read it before writing any of this day's code.
5. Shot at goal — connects the chain end to end for the first time.

Every day must be playable start to finish on its own (the "placeholder-ending rule" — end honestly on a reset/message rather than stall into not-yet-built work). Do not build day 2 before day 1 is confirmed playable by an actual human. "It builds" and "it loads" are not that confirmation — see Definition of Done below, unchanged from before.

Two rules apply to every day without exception, set by Shaun when he wrote the plan:

- Every day has the SAME number of controls. Not similar — the same. If a new mechanic seems to need a new button, the mechanic is wrong, not the control scheme.
- Players can only move STRAIGHT. There is no free steering. The compensating promise is that the ball always comes to the player's lane, so straight-line movement is always sufficient to reach it.

### Characters — narrower than "blocked," read this carefully before assuming either way

2026-08-11: Shaun's verdict on the live build was "the chaarcters have been built completly wrong," which opened `ndw-character-library#1` as the owning issue (the characters aren't owned by this repo — `ndw-roo-croc` and `ndw-safari-chase` use the same assets, so a real defect here is library-wide, not footy-specific). That issue asked a direct question — is the defect the rig, the proportions, or the art direction — and it has since been **answered by direct testing, not left open**:

- **The rig itself is not fundamentally broken.** All 22 already-rigged library characters (Octopus excepted — 6+ tentacles, confirmed by direct render that only 2 can ever map to legs) build structurally valid Unity Humanoid avatars once a real bone-mapping bug is corrected (the skeleton's Spine chain runs `Hips -> Spine02 -> Spine01 -> Spine -> shoulders`, the reverse of what the bone names suggest — this tripped up manual mapping, not Meshy's actual rig). The T-poses seen in the live v530 build are consistent with this repo's `BuildScript.cs` never assigning an `Animator.avatar` at all (`avatar: none`, confirmed by inspection) — a consuming-side gap, not proof the source rig is unusable. **Fixed**: `BuildScript.BuildHumanoidAvatar()` now assigns a real avatar to every player, verified in the actual built scene (all 6 players `isValid=True isHuman=True`), not just a standalone test.
- **Proportions differing between Croc and Roo is NOT a defect — correction to what this file said earlier.** Unity's Humanoid avatar system retargets through normalized muscle space, not raw bone length, specifically so a walk cycle scales correctly onto bodies of different proportions — that's the whole point of the abstraction. A crocodile and a kangaroo differing in height is two different animals, not drift. The earlier "identical proportions" requirement (issue #4) has been struck for exactly this reason: it's a fine rule for two footy players, a bad rule for a library that also contains a mouse and a giraffe. Do not normalize heights to "fix" this.
- **Still genuinely untested, not confirmed either way**: whether a real external animation (Mixamo or otherwise) retargets and *looks* correct once applied — every check so far has verified the skeleton is structurally capable of Humanoid retargeting, not that a real clip produces a good-looking result. Do not treat "the avatar configures" as equivalent to "the animation looks right" — run the actual visual test (drop a real clip on it, watch the limbs) before trusting a character, same discipline that caught Octopus. This is the one thing that actually decides whether the day-4 speccy is achievable.
- **Do not paper over a real rig defect with animation code if one is found.** That produced the four-armed reverts (76dbbe1, 7e02daf) once already.

Still outstanding, unresolved by any of the above: Shaun referenced a specific example of correctly rigged characters that has never reached this repo or the library issue. If it's ever supplied, judge it against issue #4's criteria — humanoid skeleton, single skinned mesh, hands and feet present, short tail — rather than assuming it invalidates the testing above.

Day 1 starts once a real external clip has been visually confirmed to retarget correctly on both Croc and Roo — not before, and not on the assumption that "the avatar configures" alone is sufficient. Proportions do not need to be unified first (see above).

Also read `neurodinoworld` issues #1 (verification tracking for the v530 shipment), #2 (positions don't match the beat), and #3 (control feels unresponsive) — real bug reports from that evaluation, useful context for what specifically went wrong even though the fix is a rebuild, not a patch.

## The recurring failure in this game

Every version of this game so far, in both the 2D and the Unity rebuilds, has broken in the same way: one fact about the world written down in two places that then drift apart. Player gravity against project gravity. Kick power against field size. Reach against where the model's hands actually are. Before tuning anything, check whether the number you are about to change has a twin somewhere else that also has to move. Issue #2 (the mark spec) restates this for jump timing specifically: the press window the player sees and the value the game grades against must be the literal same number, not two systems that are supposed to agree.

The target player is a child on a touchscreen, with real input latency between the HTML control bar and Unity. Anything that needs sub-100ms timing does not ship — issue #2 makes this concrete: nothing in this game may ever require timing tighter than 0.25s.

## Definition of done

This game is not done when it builds, not when it deploys, and not when the console is clean. None of those checks can detect the things that actually make it unplayable — a clean console has now accompanied four separate unplayable builds in this project's history (see issue #1's evaluation of v530 for the most recent).

It is done when a person plays it for five minutes and, without being coached, reaches the ball on foot, takes at least one mark, gets at least one shot at goal, never loses the ball off the field with no way to restart, and can see the ball and their own player the whole time.

Until a human has actually played it, report the status as not verified, not working. Four different claims exist and only the last one counts: it builds; it loads; a screenshot pass has seen it on screen; a human has played it. Say explicitly which applies, every time.

Add to that list: the character on screen must look like it is doing the thing it is doing. A leap that does not read as a leap is a failure even if the mark registers correctly.
