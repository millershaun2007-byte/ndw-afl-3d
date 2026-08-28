# Find which 23 Aug commit broke it — exact steps

Shaun, 28 Aug: *"i test this over and over it was fine when i left it tried to
make updates and that broke it."*

The deploy log agrees with him exactly. Do not re-litigate this; act on it.

| CI run | when (AEST) | commit | |
|---|---|---|---|
| 44 | 22 Aug 00:07 | `43b61c6` | finals series |
| 45 | 22 Aug 00:16 | `3f262fa` | last deploy before the freeze |
| | 22 Aug 00:26 | | app re-added it, quoting his sign-off |
| — | **no deploys for 4½ days** | | he tested it repeatedly — it could not have changed |
| 46 | **26 Aug 14:48** | `b0abd9e` | five unplayed 23 Aug commits land at once |

Run 46 is the event. Five gameplay commits written on 23 Aug sat unpushed for
three days, then all shipped together. **None was ever played on its own.** One
of them is very likely the regression.

## The base and the five

All six verified clean of today's confounds — `managedStrippingLevel: {}`, no
Cinemachine, so nothing from 28 Aug can muddy the result.

| # | commit | when | what it adds |
|---|---|---|---|
| 0 | `3f262fa` | 22 Aug 00:16 | **base** — the signed-off game |
| 1 | `9368bb2` | 23 Aug 06:10 | "kick falls short" scene |
| 2 | `51de5ac` | 23 Aug 06:20 | gather + snap-kick |
| 3 | `e6622d7` | 23 Aug 06:29 | real 6/1 behind scoring |
| 4 | `6f5439d` | 23 Aug 07:02 | play-on after a mark |
| 5 | `985385a` | 23 Aug 07:22 | rover chase |

They are sequential, so checking each out gives a cumulative build: 0, 0+1,
0+1+2, and so on. That is deliberate — it mirrors how they actually reached
Shaun, and the first build that reads wrong names the commit.

## Build the base too — this is the part not to skip

`BuildSceneContents` regenerates `AflMatch.unity` at build time, so **rebuilding
a commit does not reproduce that commit's build**. Measured: the archived 22 Aug
build has `WebGL.data` = 31,102,400; a rebuild of the same commit gives
31,120,864.

So do **not** compare the five rebuilds against the archived live build. Build
`3f262fa` yourself as build 0. All six must be rebuilds or the comparison is
worthless.

## Run it

One at a time. Stop the moment a build reads wrong — that is the answer.

```bash
UNITY=/Applications/Unity/Hub/Editor/6000.5.7f1-arm64/Unity.app/Contents/MacOS/Unity

for pair in 0:3f262fa 1:9368bb2 2:51de5ac 3:e6622d7 4:6f5439d 5:985385a; do
  N="${pair%%:*}"; SHA="${pair##*:}"

  git checkout --detach "$SHA"
  rm -rf Build/WebGL
  "$UNITY" -batchmode -quit -nographics -projectPath . \
           -executeMethod MainBuildScript.PerformWebGLBuild
  ./sync-to-gcs.sh "afl3d-bisect-$N"

  git checkout -- .          # the build rewrites AflMatch.unity; discard it
  echo "=== build $N ($SHA) ready: https://storage.googleapis.com/ndw-game-builds/afl3d-bisect-$N/index.html"
done
```

Then hand Shaun the URLs in order, 0 first.

## Rules while doing this

- **Never touch `gs://ndw-game-builds/afl3d/`.** The archived 22 Aug build is
  live and stays live. Every build here goes to its own `afl3d-bisect-N` path.
- **Always deploy via `sync-to-gcs.sh`.** A bare `gsutil cp` strips the `?v=`
  stamp and the browser replays a cached build — you would be comparing
  whichever builds his browser happened to keep, not these.
- **Fix nothing during the bisect.** The job is to identify the commit, not to
  repair it. A fix applied mid-run destroys the comparison.
- **Do not skip build 0.** If the base itself reads wrong, the regression is not
  in these five and the whole theory is wrong — which is worth knowing
  immediately, before five more builds.
- **Report what you built, not what you expect.** If a build fails or a size
  looks off, say so and stop.

## When it's found

The first bad build names the commit. Then, and only then:

- read that one commit's diff and say what in it explains what Shaun saw
- do not fix it in the same step — tell him what it is first

Four of these five are probably fine and worth keeping. Knowing which one is
the point of the exercise.
