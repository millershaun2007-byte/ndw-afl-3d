# AFL session notes — 28 Aug 2026

Read this before touching the footy game. Written at Shaun's request for the
next session, because a lot changed and then most of it was deliberately undone.

## Where things actually stand

| | |
|---|---|
| AFL live build | `v=90e35803ad` — wasm 16607179, data 30711486 |
| AFL source | `28f014d`, matches the live build |
| Cricket live | fixed today, loads clean in the app |
| Footy card in the app | **removed from `ndw-activities.ts`, not yet deployed** |

The footy removal is committed in the neurodinoworld repo but the app has not
been rebuilt/deployed. Either finish that or put the card back — do not leave
it half-done.

## The one thing to understand before changing anything

**Almost everything built today was reverted at Shaun's request.** Not because
it was broken — most of it worked and was verified live — but because too much
changed at once in a game that can only be judged by playing it, and the later
changes were fixing problems the earlier ones caused.

Reverted: ruck run-in and full leap, every camera distance change, Cinemachine
and the behind-the-goals shot, spoil punch, tackle, handball, two opposite
kick-in restructures, snap timings, difficulty tweaks, the ruck-skilled
defender swap, the clearer lane fix, a beat selector.

All of it is in history, `3142ed5` through `588d913`, if a single piece is ever
wanted back. **Do not bulk-restore it.**

## Standing direction from Shaun

1. **Scene by scene, in a brand new file.** He asked for this twice. I argued
   against separate `.unity` scenes (they merge badly) and offered a beat
   selector instead, then never built it. He was right about the need; the
   merge risk was the only valid part of my objection. A new file sidesteps it.
2. **No more football rules.** His words: *"in afl there is holding the ball
   ball ups it just leads to this being like a standalone game not part of a
   bigger app if you add all that stuff."* This is one activity in a kids' app.
3. **Fewer moving parts.** Tackling was removed for this reason. The handball
   too — and note his diagnosis was right: it needed a dedicated extra player
   per team, because it was borrowing `crocClearer`/`rooClearer` who have their
   own job.
4. **The snap is gone** — a mark always takes the set shot now.

## Bugs found today that will recur

**Unity WebGL caches `.data`/`.wasm` in IndexedDB keyed by URL.** The URL never
changes between builds, so a returning browser replays its cached copy no
matter what is in the bucket. `Cache-Control: no-cache` does NOT reach
IndexedDB. Every rebuild since 17 Aug had been invisible. Fixed by
`sync-to-gcs.sh`, which stamps `?v=<content-hash>` onto index.html at upload.
**Always deploy with that script, never a bare `gsutil cp`** — a plain copy
strips the stamp and the bug returns. It happened twice today.

**Absolute paths break once deployed.** Cricket looked like the wrong game; it
was the right game failing to load its characters. `const CROC =
'/models/CrocRigged.glb'` resolves against the SERVER ROOT — fine on a dev
server where the root is the game, 403 on GCS. Vite's `base: './'` does not
cover this: it rewrites the bundle's own asset URLs, not strings fetched at
runtime. Anything loaded by path at runtime must be relative itself.

**Two characters can share a spawn lane.** `rooClearer` and `rooForward` are
both at `x = 0.9` (croc pair at `-0.9`), and the clearance beat ran the clearer
to the forward's exact Z. They ended up inside one another — Shaun's words:
*"the players look likke they are having sex"*. This is a children's app. The
lane fix was reverted with everything else, so **the bug is still live**. It
predates today by weeks; pulling the camera in only exposed it.

**Uploads can partially fail.** `sync-to-gcs.sh` caught `WebGL.data` at
local=31071849 live=31071812 and refused to report success. Verify per file,
never by folder size.


## The bug that cost the whole afternoon — read this one

**Symptom:** the tap button does nothing. The beats keep running on bot rolls,
so the game plays kick-outs at you and ignores every input. Looks like "the
wrong build" or "the game is broken".

**Cause:** `managedStrippingLevel: WebGL: 3` in `ProjectSettings.asset`.

Set by `780bafb` (the Cinemachine install) to claw back the 13.7MB the package
added to the wasm. It did that — and High IL2CPP stripping also deletes managed
methods with no STATIC caller. All input here goes through the WebGL template's
`SendMessage('TouchBridge', 'TapPressed', '')`, which resolves by name at
runtime, so `TapPressed`, `SetMoveHeld` and `MarkPressed` look unreferenced.
There is no `link.xml` in `Assets/` protecting them.

**Why it survived two reverts:** both restored only `Assets/`.
`ProjectSettings/` was never touched, so every build made after 10:20 shipped
with input stripped — including builds offered as "the known working version".
It also persists on its own: `PlayerSettings.SetManagedStrippingLevel` writes
into `ProjectSettings.asset` permanently, so deleting the build-script line does
not undo it.

**Check, before blaming the game code:**

    grep -A1 managedStrippingLevel ProjectSettings/ProjectSettings.asset
    # must be `{}`, not `WebGL: 3`

    diff <(git show 89fdb33:ProjectSettings/ProjectSettings.asset) \
         ProjectSettings/ProjectSettings.asset

And verify the methods survived the build:

    strings -a Build/WebGL/Build/WebGL.data | grep -c TapPressed   # must be > 0

**If Cinemachine is ever reinstalled**, the size problem comes back. Add a
`link.xml` preserving the `SendMessage` targets BEFORE raising the stripping
level, or leave stripping at default and accept the wasm size.

## Two search mistakes worth not repeating

**Revert the right directory.** `git checkout <sha> -- Assets/` does not restore
project settings, packages, or the scene's build config. A "full revert" of this
project is `Assets/` AND `ProjectSettings/` AND `Packages/`.

**The bucket holds every build back to 17 Aug.** Object versioning is on for
`gs://ndw-game-builds`. Listing them takes one command, and the timestamps are
UTC — 19 Aug Melbourne starts at 18 Aug 14:00Z, so filtering on the UTC date
silently hides most of a day:

    gsutil ls -la gs://ndw-game-builds/afl3d/Build/WebGL.data

Identify a build by grepping its `.data` for on-screen strings ("Q1", "CATS",
"Rushed behind", "Plays on"), not by reasoning from commit messages.

## Process note from Shaun

> "i think the longer we are in sessions the less reliable you become could be
> an idea to start a new session for next time"

Fair, and the evidence is in this file. Start fresh, change one thing, let him
play it, then change the next thing.
