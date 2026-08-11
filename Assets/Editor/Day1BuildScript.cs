using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Linq;
using AFL.Day1;

// Day 1 — issue #6. A new, separate scene. Two players, one button,
// nothing else. Does not touch AflField.unity or BuildScript.cs — the
// six-player game is being retired, not merged with this, so this file
// deliberately does not share code with it beyond what's trivial to
// duplicate (loading a rigged GLB). No Animator, no Avatar, no clips —
// today's contest uses a purely procedural hop (see Day1RuckContest).
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

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(1.2f, 1f, 1.2f);
        ground.GetComponent<Renderer>().sharedMaterial = SolidColorMaterial(new Color(0.25f, 0.55f, 0.2f));

        // Real fix (2026-08-11, Shaun's direct playtest: "they also need
        // to be able to get closer to the ball and be able to reach the
        // ball at its peak") — standing this close together only works
        // because they're both static this scene (no movement, no
        // collision concern); a real contest has them shoulder to
        // shoulder under the ball, not standing well clear of it.
        var crocGo = BuildStaticCharacter("Croc", "Assets/Models/CrocRiggedAI", new Vector3(-0.55f, 0, 0), Quaternion.Euler(0, 90, 0));
        var rooGo = BuildStaticCharacter("Roo", "Assets/Models/RooRiggedAI", new Vector3(0.55f, 0, 0), Quaternion.Euler(0, -90, 0));

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
        camGo.transform.position = new Vector3(0, 1.8f, -5.5f);
        camGo.transform.LookAt(new Vector3(0, 1.6f, 0));

        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        lightGo.transform.rotation = Quaternion.Euler(55, -30, 0);

        var contestGo = new GameObject("Day1RuckContest");
        var contest = contestGo.AddComponent<Day1RuckContest>();
        contest.crocVisual = crocGo.transform;
        contest.rooVisual = rooGo.transform;
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
    static GameObject BuildStaticCharacter(string name, string modelFolder, Vector3 pos, Quaternion rot)
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
        foreach (var col in instance.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
        foreach (var smr in instance.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.updateWhenOffscreen = true;

        var animator = instance.GetComponent<Animator>() ?? instance.GetComponentInChildren<Animator>();
        if (!animator) animator = instance.AddComponent<Animator>();
        animator.avatar = null; // Generic — no Humanoid avatar, no retargeting, matches the working six-player game.
        string controllerPath = $"Assets/_Generated/{name}RiggedAIAnimator.controller";
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
