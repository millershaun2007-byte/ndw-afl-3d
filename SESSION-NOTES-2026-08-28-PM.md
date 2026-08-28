# AFL session notes — 28 Aug 2026, afternoon

Read `SESSION-NOTES-2026-08-28.md` first — it covers the morning, and everything
in it still stands. This file is the later half of the same day and **corrects
one conclusion from it**. If you only read one thing here, read
"Do not restore Assets/" below.

## TL;DR

The gameplay code has been correct all along. The bug was in
`ProjectSettings.asset`, which both of the day's reverts skipped. Fixed in
`a52a29c`. It has **not** been built, deployed or played yet.

## First: the clone is shallow, and it will lie to you

`git log` in a fresh checkout here shows **50 commits**, rooting at `f96f198`
(21 Aug 19:02). The real history is **142 commits** going back to 10 Aug.

I got the entire history wrong on my first pass because of this — concluded
kick-outs were built on 21 Aug when they actually landed **19 Aug 15:03**
(`728f2be`). Shaun corrected me from memory and he was right.

```
git fetch --unshallow origin
```

Do that before drawing any conclusion about when something was added. Three
tags only appear after it (`afl-bloopers-v1`, `afl-kick-over-heads-v1`,
`afl-mark-nonspeccy-v1`).

## What "the best version" is

Shaun named the window himself: the goal-to-goal round closing, the clock, and
the finals series.

| commit | when | what |
|---|---|---|
| `c70689e` | 21 Aug 23:18 | chained mark takes the shot — closes the goal-to-goal round |
| `c6a2537` | 21 Aug 23:56 | 3-minute quarters (the only match clock in all 142 commits) |
| `43b61c6` | 22 Aug 00:07 | 3-round finals series |

That round runs: centre bounce → ruck tap → rover run + kick → mark contest →
spoil → rushed behind at one end → kick-out from fullback → second contest →
mark → shot at goal at the far end.

**"Full ground play" is NOT `maxChainDepth = 3`.** Depth 3 was a different,
sprawling thing (kick-out → mark → clearance → mark → clearance…) that lived for
76 minutes on 21 Aug and was killed by Shaun's own playtest — "all over the
shop" (`25c5992`), narrowed again 23 minutes later (`9b01154`). The goal-to-goal
passage works fine at depth 1. Confusing the two is what broke the game at 14:55
today (`b81b8e8`) and had to be reverted an hour later.

**Do not set `maxChainDepth` to 3.** Two deliberate corrections say otherwise.

## Do not restore Assets/

This is the thing to get right. Verified by hashing, not by reading commit
messages:

- `Assets/Scripts/Day1RuckContest.cs` in the tree is **byte-identical**
  (md5 `78b38b78`) to `43b61c6` — it already *is* the best version.
- `Assets/` is otherwise exactly `c8d8649`: identical object-name census,
  identical component-type census, no files added or missing, no Cinemachine
  left in the scene. The only delta is Unity scene YAML re-serialisation churn
  (12439 lines vs 12431, equal insert/delete counts).

So `fadebb3`'s claim to have restored `Assets/` to `c8d8649` is true. Restoring
it again achieves nothing, and restoring it to `b0abd9e` (24 Aug) would *add*
the 23 Aug layer — rover chase, kick-falls-short, gather + snap-kick, 6/1
behind scoring, play-on — which Shaun did **not** name as the best version.
I was about to do exactly that; his correction stopped it.

For reference, the 23 Aug layer is byte-identical (md5 `a9d38a4d`) across
`985385a` → `b0abd9e` → `ee5f932` → `89fdb33`. Everything between those is
scene churn. If it's ever wanted back, that's one file, unambiguously.

## The actual bug: managed stripping killed the tap handler

Both reverts today restored **only `Assets/`**. Neither touched
`ProjectSettings/`. So this, set at 10:20 by the Cinemachine install
(`780bafb`), survived them both and was in every build made after 10:20 —
including the one deployed at 15:50:

```yaml
managedStrippingLevel:
  WebGL: 3        # High IL2CPP stripping
```

Why it specifically kills play:

- active template is `PROJECT:Day1` → `Assets/WebGLTemplates/Day1/index.html`
- that template drives input via
  `unityInstance.SendMessage('TouchBridge', 'TapPressed', '')`
- `SendMessage` resolves the method **by name at runtime**
- `TapPressed` (`Day1RuckContest.cs:2285`) and `AFLTouchBridge`'s
  `SetMoveHeld` / `MarkPressed` have **no static C# caller** — reflection is
  their only entry point
- there is **no `link.xml`** anywhere in `Assets/` to protect them

High stripping removes methods with no static caller. The tap button goes dead
while the beats keep running on bot rolls — so the game still plays kick-outs
at you but won't answer your input. Shaun's words: *"the one has a few kickouts
just does not work like the best version."*

Fixed in `a52a29c` by reverting to `managedStrippingLevel: {}`.
`ProjectSettings.asset` is now byte-identical to `89fdb33`, the source of the
last build he confirmed good. One file, two lines, `Assets/` untouched.

**If you ever raise stripping again, add a `link.xml` in the same commit.**
Nothing in this project protects its SendMessage entry points.

## Verification status

Against this repo's four claims — builds / deploys / screenshot-verified /
human-played — `a52a29c` currently reaches **none of them**. It is
inspection-verified only. This container is Linux with no Unity and no
`gcloud`, so it could not be built, deployed or played here.

Next session: build it, deploy it, and let Shaun play it before calling it
fixed.

## Deploying — CI is not enough on its own

`.github/workflows/build-deploy.yml` sets `Cache-Control: no-cache` on every
file but **never adds the `?v=` stamp**. Per the morning notes, `Cache-Control`
does not reach IndexedDB, which is where Unity WebGL caches `.data`/`.wasm`
keyed by URL.

The live `index.html` right now has **no `?v=` stamp** on its Build URLs — it
went up via a bare copy at 15:50. So some of today's readings may have been a
cached build rather than what was actually in the bucket.

Deploy from the Mac with `sync-to-gcs.sh`, never a bare `gsutil cp`, and never
by trusting a CI run for this particular test. Also check
`gh run list --limit 3` for an `in_progress` run before any local deploy — a CI
run finishing later will silently overwrite it with older code.

## Still open

- **Cinemachine is still in `Packages/manifest.json`** (`com.unity.cinemachine`
  3.1.7, plus `com.unity.splines` / `settings-manager` pulled in with it).
  Nothing references it — the restored scene predates it and contains zero
  Cinemachine components. Left installed deliberately: it is inert, and pulling
  it is a second change. Remove it only as its own commit.
- The `rooClearer` / `rooForward` shared spawn lane at `x = 0.9` is still live.
  Predates today by weeks. See the morning notes.
- Everything listed as open in `SESSION-NOTES-2026-08-28.md` still is.

## The pattern, again

`CLAUDE.md` opens by naming this repo's recurring failure mode: one fact about
the world written down in two places that then drift apart. Today's bug was
exactly that — a full-session revert applied to `Assets/` but not to
`ProjectSettings/`, leaving a build setting from the reverted work still in
force. Two of today's three bugs were this shape. When you revert a session,
revert every directory it touched, not just the one with the game code in it.
