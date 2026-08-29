# The power system — one rule for the whole game

Shaun, 29 Aug 2026: *"there actually has to be a rhyme and reason for the game
like tap to the power to take a speccy tap to this power to spoil"*, and
*"these things should be set up first then we work on the game again."*

**Set this up before building anything else on top of it.** Every beat added
later inherits it for free. That is the point.

## The problem this fixes

The game currently has FOUR different meanings for the same tap:

| beat | what a tap currently means |
|---|---|
| ruck | mash to fill a power bar (`_ruckPower`) |
| mark | one tap, timed inside `markPerfectWindow` |
| spoil | 1–3 taps, best one inside `defenderSpoilWindow` |
| shot | one tap on a rising band (`shotPowerGreenMin/Max`) |

Four rules, none of which teaches the next. And the best moment in the game is
not earned at all:

```csharp
bool isSpeccy = Random.value < speccyChance;   // 0.3f
```

A coin flip. The player cannot go for a speccy, which is exactly backwards.

## The one rule

**Tap to build power. Power decides the outcome. Stop tapping and it drains.**

That is the whole grammar. It is already implemented as `_ruckPower` +
`ruckTapGain` + `ruckDecayPerSec`, already clamped 0–1, and already drawn on
screen as a traffic-light bar. Generalise it — do not write a second one.

Rename to `_power` / `powerTapGain` / `powerDecayPerSec` and let every beat read
it. One meter, one bar, one thing to learn in the first ten seconds.

## What power buys, per beat

| beat | low | mid | full |
|---|---|---|---|
| **mark** | spilled | normal mark | **speccy** |
| **spoil** | missed, mark stands | fist gets a piece | clean punch through |
| **ruck** | bot wins the tap | scrappy | clean tap to your rover |
| **run** | caught | held up | **breaks clear** |
| **shot** | dribbles short | on line | clean goal |

Same bar, same gesture, five different outcomes. A child learns it once.

### Running is the same rule, inverted pressure

Shaun: *"with running and kicking they have a power as well they run a certain
speed they get clear they dont go that speed they get caught opposite mechanism
no tackling though."*

Power = **speed**. Fill it and the runner breaks clear; let it drain and the
chaser closes and the kick falls short. It is the same tap doing the same thing,
just with the pressure coming from behind instead of from above.

**No tackling.** Getting caught means the kick falls short — the
`ShortKickLanding` outcome that already exists. Nobody gets brought down.
Removing the tackle was a deliberate call (`159d386`, "fewer moving parts") and
it stays removed.

This also fixes the biggest dead patch in the game: `RunStraight` currently
contains **zero input checks**, so every running section is a cutscene.

### The snap is the deliberate exception

Shaun: *"snaps just a quick tap."*

No meter. Gather, tap, it's away. That is what makes a snap feel like a snap —
it is the one beat where there is no time to build anything, and the contrast is
what gives it its character. Do not add a bar to it.

### The speccy becomes something you go for

Delete `speccyChance`. `isSpeccy` becomes "the bar was full at the moment of
truth". The forward flies because the child earned it, not because a random
number said so.

This is the single biggest improvement in the document.

### The centre clearance decides the next contest

Shaun: *"with the kick out of the centre the next contest being a speccy or
normal mark is dependent on what the child taps."*

Power at the moment of the clearing kick carries forward into the contest it
sets up. A big clearance sets up a speccy; a scrappy one sets up a scramble.
The beats stop being independent set pieces and start being consequences of each
other — which is what "flow" actually means here.

## Risk — do not skip this

If more power is always better, filling the bar is always correct and there is
no decision, only mashing. Power needs a cost at the top.

**Overcommit.** Push past full and you go too early or too hard:

- mark: you fly over the top of it, ball goes through
- spoil: you punch through thin air, mark stands
- run: you overrun the ball
- shot: you blaze it

The traffic light already has the vocabulary for this — green is the band, past
green is red again. Same bar, same colours, now with a reason to stop tapping.

## Build order — one beat at a time

Do NOT do these in one session. One per build, played by Shaun before the next.

1. **Generalise the meter.** Rename `_ruckPower` → `_power`, make the bar draw
   for any beat that asks for it. No behaviour change anywhere. Ship it, confirm
   the ruck still plays exactly as before.
2. **Mark.** Power decides spilled / normal / speccy. Delete `speccyChance`.
   This is the one that proves the whole idea.
3. **Spoil.** Power decides missed / partial / clean.
4. **Run.** Power = speed, drain = the chaser closes.
5. **Shot.** Replace the rising band with the same meter.
6. **Overcommit.** Add the top-end penalty across all of them at once, since it
   only makes sense as one rule.
7. **Clearance carry-over** — the centre-clearance power feeding the next
   contest. Last, because it depends on 2 and 4 both being right.

## Rules while building this

- One beat per commit, one commit per build, one build per playtest.
- Never build on a version Shaun has not played.
- If a beat needs a second control, the design is wrong — it is one tap.
- Keep every tunable as its own named field. Do not reuse another beat's
  constant, ever. That single habit caused the set-shot arc on the spoil punch,
  `Abs()` on the range test, and the hardcoded 1.4 against a derived 1.65.
