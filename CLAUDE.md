# ndw-afl-3d (Mount Duneed Cats Footy)

Unity WebGL game. The built player ships into the NeuroDinoworld app at www/unity-games/afl3d/ and is embedded there as an iframe.

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
