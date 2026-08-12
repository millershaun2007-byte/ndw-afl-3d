using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using System.Linq;
using AFL.Day1;

// Day 1/2 — issue #6 and the canonical rebuild plan (issue #1 pinned
// comment). One persistent scene, not per-day scenes — "each day adds a
// capability to that same world," so day 2 (the rovers) extends this same
// file and Day1RuckContest rather than branching into parallel Day2
// files. Does not touch AflField.unity or BuildScript.cs — the six-player
// game is being retired, not merged with this.
public static class Day1BuildScript
{
    public static void PerformWebGLBuild()
    {
        BuildSceneContents(saveToDisk: true);

        var outputPath = System.Environment.GetEnvironmentVariable("NDW_BUILD_OUTPUT") ?? "Build/WebGL";
        System.IO.Directory.CreateDirectory(outputPath);
        PlayerSettings.productName = "Mount Duneed Cats Footy";
        PlayerSettings.WebGL.template = "PROJECT:Day1";
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Day1Ruck.unity" },
            locationPathName = outputPath,
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        });

        var summary = report.summary;
        Debug.Log($"NDW_BUILD_RESULT={summary.result} totalSize={summary.totalSize} totalTime={summary.totalTime} errors={summary.totalErrors} warnings={summary.totalWarnings}");
        if (summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
    }

    public static void BuildSceneContents(bool saveToDisk)
    {
        AssetDatabase.Refresh();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Real fix (2026-08-12, Shaun: "the ground is also a rectangle not
        // an oval which could be causing these problems... would it be too
        // much to make the ground an oval"). A flat Plane is inherently
        // rectangular — no scale trick fixes that. A flattened Cylinder
        // has a genuine circular/elliptical edge instead, so scaling it
        // unevenly on X/Z gives a real oval outline, not a texture trick.
        // Default Cylinder is radius 0.5 (diameter 1) and height 2
        // (y -1..+1) at scale 1, so localScale.x/z here are actual world
        // diameters, not the old Plane's "x10" multiplier — kept roughly
        // the same real-world footprint as the previous 36x52 rectangle
        // (radius 18 x 26 => diameter 36 x 52). Flattened to a thin disc
        // (scale.y 0.05) and dropped so its TOP surface still sits at
        // world y=0, same as the old Plane, so nothing else on the
        // ground (characters, ball) needs repositioning.
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ground.name = "Ground";
        ground.transform.position = new Vector3(0, -0.05f, 0);
        // Real fix (2026-08-12, Shaun: "why dont we make the field less
        // wide but just long... as we are only going in straight lines to
        // compensate"). The control rule (canonical plan) is straight-
        // line movement only, forever — width was never going to be used
        // for anything, no player ever moves sideways. Halved from 36 to
        // 18; length unchanged for now.
        ground.transform.localScale = new Vector3(18f, 0.05f, 52f);
        ground.GetComponent<Renderer>().sharedMaterial = SolidColorMaterial(new Color(0.25f, 0.55f, 0.2f));
        // CreatePrimitive(Cylinder) adds a CapsuleCollider by default,
        // which is the wrong shape for a flat disc and misleading to
        // leave around — nothing collides with the ground yet anyway.
        Object.DestroyImmediate(ground.GetComponent<Collider>());

        // Real fix (2026-08-11, Shaun's direct playtest: "they also need
        // to be able to get closer to the ball and be able to reach the
        // ball at its peak") — standing this close together only works
        // because they're both static this scene (no movement, no
        // collision concern); a real contest has them shoulder to
        // shoulder under the ball, not standing well clear of it.
        // Facing, settled 2026-08-12 after several wrong guesses this
        // session (worth keeping the reasoning, not just the final
        // numbers, since it took genuine back-and-forth to get right):
        //
        // - The ball sits at x=0, directly BETWEEN Croc (x=-0.55) and Roo
        //   (x=0.55) — on the X axis, not Z. Any Z-facing choice for the
        //   ruck pair puts the ball to their SIDE, not front or back, and
        //   which side reads as "back" depends on rig quirks — this is
        //   what caused "crocodile players have there back to the ball."
        //   The only orientation that guarantees neither ruck player is
        //   ever facing away from the ball is facing each other directly
        //   across it — the original pre-2026-08-12 rotation, restored
        //   here for the ruck pair specifically.
        // - Shaun's oval diagram confirmed goals sit at the far/near ends
        //   (Z, this camera's depth direction) not the sides (X). That's
        //   real for the ROVERS, who aren't contesting anything right now
        //   and should read as oriented toward their own attacking end —
        //   so they keep the Z-axis, opposite-ends facing from the oval
        //   fix, unaffected by the ruck pair's ball-facing requirement.
        var crocGo = BuildStaticCharacter("Croc", "Assets/Models/CrocRiggedAI", new Vector3(-0.55f, 0, 0), Quaternion.Euler(0, 90, 0));
        var rooGo = BuildStaticCharacter("Roo", "Assets/Models/RooRiggedAI", new Vector3(0.55f, 0, 0), Quaternion.Euler(0, -90, 0));

        // Day 2 (2026-08-11, Shaun: "the next step would be the person in
        // the ruck to tap the ball to a player" / "one more player from
        // each team inserted"). Same models as their side's ruck — no new
        // character asset needed, matches the spec's "not a new mechanic"
        // framing.
        //
        // Facing/position, settled 2026-08-12 (see the long comment above
        // Croc/Roo for the full reasoning): rovers aren't contesting the
        // ball, so they face their own team's attacking end (Z axis, per
        // Shaun's oval diagram) rather than the ball itself, and sit
        // "behind" their ruck teammate on that same axis (Shaun: "the
        // rovers can stand behind if they want") — behind Croc (attacking
        // +Z) is -Z; behind Roo (attacking -Z) is +Z.
        var crocRoverGo = BuildStaticCharacter("CrocRover", "Assets/Models/CrocRiggedAI", new Vector3(-1.7f, 0, -1.8f), Quaternion.identity);
        var rooRoverGo = BuildStaticCharacter("RooRover", "Assets/Models/RooRiggedAI", new Vector3(1.7f, 0, 1.8f), Quaternion.Euler(0, 180, 0));

        // Day 4 setup (2026-08-12, Shaun: "we will need 2 more players
        // defender and forward", confirmed "4 [total]... 2 more at each
        // end", then "use differnt characters please"). Visual only for
        // now — same incremental pattern as day 2's rovers (added to the
        // scene first, wired into contest logic as a separate step once
        // positioning is confirmed). One contest zone per end of the
        // ground, each with the attacking team's forward and the
        // OPPOSING team's defender — when Croc's rover kicks toward
        // Croc's own attacking end (+Z), it's Croc's forward contesting
        // Roo's defender there, not a Croc-vs-Croc pairing. Species: each
        // team's extra players use one new species distinct from their
        // own ruck (Dragon for Croc's side, Lion for Roo's side) rather
        // than reusing Croc/Roo — both already confirmed good via direct
        // render earlier in this project, not untested picks. Positioned
        // inside the goal posts (±24) roughly where a kick from the
        // rover's run currently lands — approximate on purpose, expected
        // to need tuning once the mark itself exists and kick distance
        // gets dialled in for real.
        //
        // Real fix (2026-08-12, Shaun: "the forwards are going to have to
        // come up further or the croc runs up further", "or the
        // kangaroo" — same gap on both sides). z=15 was too far from
        // where the rover actually STOPS running (~z=4, before the kick
        // carries the ball the rest of the way) — the whole tableau read
        // as disconnected even though the kick itself bridges the gap.
        // Brought in to z=10 rather than touching the run/kick pacing,
        // which was already confirmed good.
        EnsureAnimatorController("Dragon", "Assets/Models/DragonRiggedAI");
        EnsureAnimatorController("Lion", "Assets/Models/LionRiggedAI");
        // Real fix (2026-08-12, Shaun: "the characters inserted are much
        // smaller"). Measured directly (Renderer bounds), not guessed:
        // Croc=2.098 high, Roo=2.317, Dragon=1.937 (92% of Croc), Lion=
        // 1.710 (74-82% of Croc/Roo) — a real, different native scale
        // baked into this Meshy batch vs Croc/Roo's. Corrected to match
        // Croc's height as the reference.
        //
        // Real fix (2026-08-12, Shaun: "just need to get the characters
        // to look a little bigger, in the forward line and defence") —
        // matching Croc's real height wasn't enough on its own: the
        // forward/defender zone stands further from the fixed camera
        // (z=10) than the ruck (z=0), so perspective alone makes them
        // read smaller even at equal real-world height. Boosted further
        // to compensate for that distance, not just parity.
        const float dragonScale = 1.083f * 1.3f;
        const float lionScale = 1.227f * 1.3f;
        var crocForwardGo = BuildStaticCharacter("CrocForward", "Assets/Models/DragonRiggedAI", new Vector3(-0.9f, 0, 10f), Quaternion.identity, dragonScale);
        var rooDefenderGo = BuildStaticCharacter("RooDefender", "Assets/Models/LionRiggedAI", new Vector3(0.9f, 0, 10f), Quaternion.Euler(0, 180, 0), lionScale);
        var rooForwardGo = BuildStaticCharacter("RooForward", "Assets/Models/LionRiggedAI", new Vector3(0.9f, 0, -10f), Quaternion.Euler(0, 180, 0), lionScale);
        var crocDefenderGo = BuildStaticCharacter("CrocDefender", "Assets/Models/DragonRiggedAI", new Vector3(-0.9f, 0, -10f), Quaternion.identity, dragonScale);

        // Goal posts (2026-08-12, Shaun: "chuck up those... goal posts").
        // Visual only — no collision, no scoring trigger, that's still
        // genuinely day 5's work (the actual shot-at-goal mechanic). But a
        // static prop standing at each end doesn't presuppose or hook into
        // that mechanic any more than the ground or the oval shape do, so
        // there's no reason it has to wait. Real AFL layout: 2 tall inner
        // goal posts + 2 shorter outer behind posts per end, placed just
        // inside the oval's Z boundary (radius 26).
        BuildGoalPosts(new Vector3(0, 0, 24f));
        BuildGoalPosts(new Vector3(0, 0, -24f));

        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "Ball";
        ball.transform.position = new Vector3(0, 1f, 0);
        ball.transform.localScale = new Vector3(0.35f, 0.24f, 0.35f);
        ball.GetComponent<Renderer>().sharedMaterial = SolidColorMaterial(new Color(0.6f, 0.35f, 0.15f));
        Object.DestroyImmediate(ball.GetComponent<Collider>());

        var camGo = new GameObject("Main Camera");
        var cam = camGo.AddComponent<Camera>();
        cam.tag = "MainCamera";
        cam.backgroundColor = new Color(0.55f, 0.75f, 0.95f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        // Real fix (2026-08-12, Shaun: "not quite as big as the old game").
        // Enlarging the ground plane alone did nothing visible — the camera
        // was fixed close to the ruck, so nearly all of the extra ground
        // sat outside its frame regardless of how big the plane actually
        // was. Pulled back and raised so a genuinely wider field is
        // visible, closer to the old game's broader view.
        //
        // Reverted (2026-08-12, Shaun: "thats ruined the game bring it
        // back to the way it was before that change") — the further
        // pullback tried right after this (both ends + forward/defender
        // visible) looked fine in a static render but was wrong in
        // actual play. Back to the position confirmed good before that.
        camGo.transform.position = new Vector3(0, 3.4f, -9.5f);
        camGo.transform.LookAt(new Vector3(0, 1.2f, 0));

        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        lightGo.transform.rotation = Quaternion.Euler(55, -30, 0);

        var contestGo = new GameObject("Day1RuckContest");
        var contest = contestGo.AddComponent<Day1RuckContest>();
        contest.crocVisual = crocGo.transform;
        contest.rooVisual = rooGo.transform;
        contest.crocRover = crocRoverGo.transform;
        contest.rooRover = rooRoverGo.transform;
        contest.crocForward = crocForwardGo.transform;
        contest.rooDefender = rooDefenderGo.transform;
        contest.rooForward = rooForwardGo.transform;
        contest.crocDefender = crocDefenderGo.transform;
        contest.ball = ball.transform;

        var bridgeGo = new GameObject("TouchBridge");
        bridgeGo.AddComponent<Day1TouchBridge>();

        if (saveToDisk)
        {
            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Day1Ruck.unity");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Day1Ruck.unity", true) };
        }
    }

    // Real fix (2026-08-12) — Croc/Roo's existing controllers
    // (Assets/_Generated/CrocRiggedAIAnimator.controller etc.) use a
    // DIFFERENT naming/threshold convention than BuildScript.cs's own
    // reusable BuildAnimatorController ({species}Animator.controller,
    // 0.3 threshold, vs the real files being {species}RiggedAIAnimator
    // .controller at 4.55) — they weren't generated by that method, some
    // other process made them. Rather than chase that inconsistency,
    // this generates a fresh controller at the EXACT path
    // BuildStaticCharacter's controller lookup already expects, for any
    // new species that doesn't have one yet. Idempotent — skips if the
    // asset already exists, so calling it every build is cheap and safe.
    static void EnsureAnimatorController(string species, string modelFolder)
    {
        string path = $"Assets/_Generated/{species}RiggedAIAnimator.controller";
        if (AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(path)) return;

        System.IO.Directory.CreateDirectory("Assets/_Generated");
        var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(path);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Airborne", AnimatorControllerParameterType.Bool);
        controller.AddParameter("HasBall", AnimatorControllerParameterType.Bool);
        controller.AddParameter("KickCharge", AnimatorControllerParameterType.Float);
        var sm = controller.layers[0].stateMachine;
        var idle = sm.AddState("Idle");
        sm.defaultState = idle;

        var walkClip = FindClipInFolder(modelFolder, "Walking");
        if (walkClip != null)
        {
            var walk = sm.AddState("Walk");
            walk.motion = walkClip;
            var toWalk = idle.AddTransition(walk);
            toWalk.hasExitTime = false; toWalk.duration = 0.15f;
            toWalk.AddCondition(AnimatorConditionMode.Greater, 0.3f, "Speed");
            var toIdle = walk.AddTransition(idle);
            toIdle.hasExitTime = false; toIdle.duration = 0.15f;
            toIdle.AddCondition(AnimatorConditionMode.Less, 0.3f, "Speed");
        }
        else
        {
            Debug.LogWarning($"EnsureAnimatorController: no Walking clip found in {modelFolder} for {species} — Idle only.");
        }
        EditorUtility.SetDirty(controller);
    }

    static AnimationClip FindClipInFolder(string folder, string suffix)
    {
        string glbPath = System.IO.Directory.GetFiles(folder, "*" + suffix + ".glb").FirstOrDefault();
        if (glbPath == null) return null;
        glbPath = glbPath.Replace('\\', '/');
        return AssetDatabase.LoadAllAssetsAtPath(glbPath).OfType<AnimationClip>().FirstOrDefault();
    }

    // Visual-only goal posts (2026-08-12, Shaun: "chuck up those... goal
    // posts"). Real AFL layout: 2 tall inner goal posts (score 6) + 2
    // shorter outer behind posts (score 1) per end. No collider, no
    // scoring trigger — that's genuinely day 5's work.
    static void BuildGoalPosts(Vector3 centre)
    {
        BuildPost(centre + new Vector3(-1.3f, 0, 0), 3.2f, 0.09f);
        BuildPost(centre + new Vector3(-0.6f, 0, 0), 3.2f, 0.09f);
        BuildPost(centre + new Vector3(0.6f, 0, 0), 3.2f, 0.09f);
        BuildPost(centre + new Vector3(1.3f, 0, 0), 3.2f, 0.09f);
    }

    static void BuildPost(Vector3 basePos, float height, float radius)
    {
        var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        post.name = "GoalPost";
        post.transform.position = basePos + Vector3.up * (height / 2f);
        post.transform.localScale = new Vector3(radius * 2f, height / 2f, radius * 2f);
        post.GetComponent<Renderer>().sharedMaterial = SolidColorMaterial(Color.white);
        Object.DestroyImmediate(post.GetComponent<Collider>());
    }

    // Real fix (2026-08-11, Shaun playtest: "arms not raised" / static-
    // looking characters). This used to destroy every Animator on the
    // model, reasoning that Day 1 has no Mixamo clips to retarget so no
    // avatar/animator was needed — that premise was wrong on inspection:
    // there is no Mixamo anywhere in this repo (confirmed by grep), the
    // "no Mixamo" line in Day1RuckContest.cs's header was chasing a tool
    // this project never used. The real, already-built animation
    // infrastructure is Generic (bone-name matching, not Humanoid/muscle
    // retargeting) — Assets/_Generated/CrocRiggedAIAnimator.controller and
    // RooRiggedAIAnimator.controller already exist and already work (this
    // is exactly what the six-player game's BuildScript.cs ships with).
    // A direct render test (AvatarRetargetTest.cs, kept for the record)
    // confirmed: this Generic path produces a natural, correct-looking
    // walk-cycle pose on both Croc and Roo; a Humanoid-avatar-based
    // attempt at the same clip did not (a real, separate defect, not
    // relevant here since nothing in this game needs Humanoid retargeting
    // — there is no external clip to retarget). So: keep the Animator,
    // assign the real controller, hold it on Idle for a natural resting
    // pose instead of the frozen import-time bind pose. Day1RuckContest
    // still drives the reach/tap gesture procedurally (no clip exists for
    // that specific motion) — it disables the Animator for the duration
    // of each hop so the two don't fight over the same bones, then
    // re-enables it after.
    static GameObject BuildStaticCharacter(string name, string modelFolder, Vector3 pos, Quaternion rot, float scale = 1f)
    {
        string riggedPath = System.IO.Directory.GetFiles(modelFolder, "*Rigged.glb").FirstOrDefault();
        var root = new GameObject(name);
        root.transform.position = pos;
        root.transform.rotation = rot;

        if (riggedPath == null)
        {
            var fallback = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            fallback.name = "Visual";
            fallback.transform.SetParent(root.transform, false);
            fallback.transform.localPosition = Vector3.up;
            return root;
        }

        riggedPath = riggedPath.Replace('\\', '/');
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(riggedPath);
        var instance = Object.Instantiate(prefab, root.transform);
        instance.name = "Visual";
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one * scale;
        foreach (var col in instance.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
        foreach (var smr in instance.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.updateWhenOffscreen = true;

        var animator = instance.GetComponent<Animator>() ?? instance.GetComponentInChildren<Animator>();
        if (!animator) animator = instance.AddComponent<Animator>();
        animator.avatar = null; // Generic — no Humanoid avatar, no retargeting, matches the working six-player game.
        // Real fix (2026-08-12) — this used to derive the controller
        // filename from `name`, the character's own display/GameObject
        // name ("Croc", "Roo", but also "CrocRover", "RooRover"), which
        // only happened to work for the ruck pair by coincidence. For the
        // rovers it built a path to a file that's never existed
        // ("CrocRoverRiggedAIAnimator.controller"), so LoadAssetAtPath
        // silently returned null and every rover has been running with no
        // controller at all since rovers were introduced — confirmed via
        // a live console log (Playwright): animator.runtimeAnimatorController
        // was NULL on the deployed build, which is why no amount of Speed-
        // parameter tuning ever changed anything. The species (which
        // determines the real controller filename) is reliably the model
        // folder name, not the character's own display name.
        string species = System.IO.Path.GetFileName(modelFolder).Replace("RiggedAI", "");
        string controllerPath = $"Assets/_Generated/{species}RiggedAIAnimator.controller";
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
        if (controller) animator.runtimeAnimatorController = controller;
        else Debug.LogWarning($"Day1BuildScript: no controller at {controllerPath} for {name}, leaving Animator uncontrolled.");

        return root;
    }

    static Material SolidColorMaterial(Color color)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        mat.color = color;
        return mat;
    }
}
