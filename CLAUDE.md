# ndw-afl-3d (Mount Duneed Cats Footy)

Unity WebGL game. The built player ships into the NeuroDinoworld app at www/unity-games/afl3d/ and is embedded there as an iframe.

## Before the next pass, read the FIX BRIEF

Issue #1 has a comment headed "FIX BRIEF - read this before writing any code". That is the current working brief: what the deployed build actually does when a person plays it, which failures are blockers, the numeric landmines, the slice order to build in, and how to verify. Run `gh issue view 1 --comments` and read it before starting. The comment above it, "Verified by playing the live build", lists the symptoms as observed on screen.

Blocking question, answer it first: do the character models have skeletons and animation clips? Every player in the live build stands in a permanent T-pose with arms detached from shoulders, tails floating away from the body and no feet. That is an asset or import problem, not a gameplay-code problem, and no gameplay work should start until it is answered in issue #1.

Also currently broken in the live build: MOVE does nothing at all, the MARK button fires a kick while KICK does something else, and the match deadlocks after the first goal so it can never reach 5. All of that happens with a completely clean console.

Live build: https://millershaun2007-byte.github.io/neurodinoworld/www/unity-games/afl3d/index.html

## Read this first

docs/FOOTY-REBUILD.md is the standing plan for this game, and issue #1 is the full spec behind it. The game is being cut back from a full match sim to one loop: centre throw-up, ruck tap to the rover, clearance kick to the forward, contest for the mark, then a set shot at goal. First team to 5 goals wins. Three buttons only: MOVE, MARK, KICK.

Do not add tackles, handball, a clock, quarters or a behind tally back in without checking issue #1 first. They were removed deliberately.

## The recurring failure in this game

Every version of this game so far, in both the 2D and the Unity rebuilds, has broken in the same way: one fact about the world written down in two places that then drift apart. Player gravity against project gravity. Kick power against field size. Reach against where the model's hands actually are. Before tuning anything, check whether the number you are about to change has a twin somewhere else that also has to move.

The target player is a child on a touchscreen, with real input latency between the HTML control bar and Unity. Anything that needs sub-100ms timing does not ship.

## Definition of done

This game is not done when it builds, not when it deploys, and not when the console is clean. None of those checks can detect the things that actually make it unplayable, which is why pass after pass has been declared verified and then reversed by a real-device report: the joystick, the D-pad, the four-armed ruck animation, and the 2026-08-11 play session.

It is done when a person plays it for five minutes and, without being coached, reaches the ball on foot, takes at least one mark, gets at least one shot at goal, never loses the ball off the field with no way to restart, and can see the ball and their own player the whole time.

Until a human has actually played it, report the status as not verified, not working. Issue #1 has the full symptom list from the 2026-08-11 session.
