# Footy (ndw-afl-3d) — rebuild plan

Canonical spec: issue #1. Read that before changing this game.

## The loop

Centre throw-up, ruck jumps and taps to the rover, rover gathers and kicks to the forward, forward contests the mark. Mark taken means a set shot at goal; mark lost means no shot and straight back to the centre. First team to 5 goals wins.

Three buttons: MOVE, MARK, KICK. No tackles, no handball, no clock, no quarters, no behind tally.

Control hands off automatically at each link (ruck, rover, forward, kicker) and the camera cuts at each handoff instead of chasing whoever has the ball.

## Constants that must agree

Project gravity and AFLPlayer.gravity must be the same value, around -14. They were -9.81 and -24, so the ball hung about 2.5 times longer than the jump could reach it. That is why the jump timing never felt right.

Kick range must fit the ground. The field is a 35x45 plane with goals 40 apart, so a full kick should travel about 28m and the shortest about 8m. It was 12m to roughly 85m.

minMarkDistance must be reachable on that ground, around 8 rather than 15, or marks never pay.

standingReach, handsAnchor and ballHold must match where the visual model's hands actually are. BuildScript instantiates the GLB at localScale 2 with a +1 offset, so the physics reach and the visible hands are not the same thing unless one is derived from the other.

Anything that is one fact written down in two places has broken this game at least once. Derive it, do not duplicate it.

## Standing rules

Every phase needs its own reset. There is no boundary rule in the physics at all, so a kick that leaves the plane must never be able to stall the game.

Aim comes from the player's facing, never from the camera. No mouse-look, and taps on the canvas must not charge or fire kicks.

Bots must be beatable by a child on a touchscreen, allowing for the touch-bridge input latency that a bot does not pay.

Commit per change, as this repo already does.
