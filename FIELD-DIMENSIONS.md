IMPORTANT FIELD DIMENSIONS

This Unity AFL field is NOT a full-size oval.

It is a rectangular arcade field:

WIDTH:
35 Unity units

LENGTH:
45 Unity units

Unity Plane scale:
(3.5, 1, 4.5)

FIELD LIMITS:

X:
-17.5 to +17.5

Z:
-22.5 to +22.5

Do not use real AFL metre distances.

All gameplay distances must be scaled to this small field.

## GOALS

Goal lines are at:

Team A end:
Z = -20

Team B end:
Z = +20

There are approximately 2.5 units of turf behind each goal line.

The playable goal-to-goal distance is therefore approximately:

40 Unity units.

## CENTRE

Centre is:

X = 0
Z = 0

Centre circle diameter:

6 Unity units

The ruck players begin approximately:

Ruck A:
X = -0.55

Ruck B:
X = +0.55

Keep the existing working ruck setup.

Do NOT rebuild it.

## PLAYER STARTING AREAS

Existing player positioning uses approximately:

Forwards / defenders:
Z = ±5
and
Z = ±10

Clearance players:
Z = ±13

Use the existing actual scene/player positions where possible rather than spawning duplicate players.

There are only 9 players total on the field.

Do not build logic for 18 players per team.

## FIELD SCALE RULES

Because the field is only 45 units long:

A 16-unit kick travels about 35–40% of the ground.

Therefore:

VERY SHORT movement/pass:
2–5 units

SHORT run:
3–6 units

LONG run:
6–10 units

SHORT kick:
6–10 units

NORMAL kick:
10–14 units

LONG kick:
14–18 units

VERY LONG kick:
18–21 units maximum

Do not use 30-, 40- or 50-unit kick distances.

Those distances are inappropriate for this field.

## DEFENSIVE ZONE

Do NOT use:

defensiveZoneZ = -25

because -25 is outside the field.

For a team attacking toward +Z:

Deep defence:
Z approximately -20 to -13

Defensive transition:
Z approximately -13 to -6

Midfield:
Z approximately -6 to +6

Attacking transition:
Z approximately +6 to +13

Forward area:
Z approximately +13 to +20

For a team attacking toward -Z, mirror these values.

Do not hard-code one team's field logic without mirroring it for the other team.

## GOAL RANGE

Do NOT use:

runningGoalRange = 45

because the entire ground is only 45 units long.

Use approximately:

Close snap:
3–7 units from goal

Normal running shot:
5–10 units from goal

Long goal:
10–14 units from goal

Maximum arcade shot:
approximately 15–16 units from goal

A player at midfield must NOT automatically be considered in goal range.

## CENTRE CLEARANCE

After the existing ruck:

Ruck tap
→ clearance midfielder chases
→ opposition player chases without tackling
→ midfielder picks up
→ runs approximately 2–4 units
→ kicks approximately 10–16 units toward attacking end.

Because the field is small, do not make the clearance kick travel 25–40 units.

A 12–16 unit clearance is already a strong kick.

## END-TO-END PLAY

For a team attacking from -Z toward +Z, a simple full-field play can look like:

Defensive possession:
Z ≈ -18

First movement:
Z ≈ -13

First receiving area:
Z ≈ -7

Midfield:
Z ≈ 0

Forward entry:
Z ≈ +8 to +12

Forward possession:
Z ≈ +13 to +17

Goal:
Z = +20

Example:

Defender at -17
→ kick about 10 units
→ teammate near -7
→ run 3–5 units
→ kick about 14–16 units
→ forward around +10
→ run toward +15
→ shot or snap at goal.

This means an end-to-end play should generally require 2–3 meaningful possessions rather than one enormous kick.

## PLAYER SPREAD

Because there are only 9 players TOTAL:

Do not make all players chase the football.

Usually:

1 player has possession

1 opposition player provides pressure/chases

1 nearby teammate can become a passing option

remaining players hold useful positions upfield/downfield.

Keep the field visually open.

## NO TACKLING

There is no tackling.

Opposition pressure means:

* chase
* close distance
* run alongside
* block space
* force quicker kicks

Opponents must NOT:

* tackle
* knock the player over
* attach to the player
* stop possession through physical collision

## KICK DISTANCE

Existing:

kickDistance = 16

is already a LONG kick on this ground.

Keep that scale in mind throughout every script.

Do not introduce values based on real-world AFL distances.

## CINEMACHINE

Use Cinemachine 3.

The gameplay camera must respect the small field dimensions.

Do not zoom so far out that the entire 35 × 45 field is visible during normal gameplay.

Normal gameplay should frame:

* ball carrier
* nearby opponent
* nearby teammate
* some space ahead

During a 14–16 unit long kick:

allow Cinemachine to follow the ball toward the next play.

Do not create unnecessary additional cameras.

## ABSOLUTE RULE

Before adding any movement, kick, shot, defensive zone or AI distance:

compare the number against these actual field dimensions:

X = -17.5 to +17.5
Z = -22.5 to +22.5
Goals = Z ±20

If a distance is a large fraction of 45 units, treat it as a very large gameplay distance.

Never assume Unity units correspond directly to real AFL metres.
