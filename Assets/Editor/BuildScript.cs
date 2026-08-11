using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using AFL;

// Rebuild of BuildScript for the AFLGameKit.cs architecture (2026-08-10 full
// rewrite — see the archived project ndw-afl-3d-ARCHIVED-2026-08-10 for the
// old single-lane/Ruck-Rover system this replaces). Same programmatic-scene
// pattern as before: construct everything in code, save a .unity scene, then
// do a headless WebGL build. Invoked via:
//   Unity -batchmode -quit -executeMethod BuildScript.PerformWebGLBuild
//
// 2026-08-11: scoped down further per issue #1 / docs/FOOTY-REBUILD.md — the
// 5-goal-chain rebuild (centre throw-up -> run/kick to a forward option ->
// mark contest -> set shot). See AFLGameManager for the phase logic; the
// changes here are: real physics gravity to match AFLPlayer's, goal trigger
// boxes tall enough to actually catch a lofted kick, hand/ball anchors
// derived from each model's real bounds instead of a hardcoded guess, and
// every player gets a bot brain (not just the non-human ones) so control
// handing away from someone never leaves them standing still forever.
public static class BuildScript
{
    // Real physics-layer separation (2026-08-10, direct review feedback):
    // ball/players/ground all sat on the single Default layer, which meant
    // the camera's default collisionMask (Default) treated the followed
    // player's own CharacterController as an obstruction — the classic
    // "camera jams into your own back" bug — and the kinematic ball
    // parented to a carrier's hand kept generating real collisions against
    // that same carrier's CharacterController, causing stutter. Three
    // dedicated layers fix both: Ground for terrain/scenery (what the
    // camera is allowed to collide with), Player for every AFLPlayer's
    // CharacterController (what AFLBall.playerMask scans for contests),
    // and Ball for the ball itself (so Ball×Player can be disabled in the
    // physics matrix below without touching Ball×Ground).
    static int _groundLayer, _playerLayer, _ballLayer;

    public static void PerformWebGLBuild()
    {
        BuildSceneContents(saveToDisk: true);

        var outputPath = System.Environment.GetEnvironmentVariable("NDW_BUILD_OUTPUT") ?? "Build/WebGL";
        System.IO.Directory.CreateDirectory(outputPath);

        // Ship UNCOMPRESSED — per neurodinoworld/CLAUDE.md's "Unity WebGL
        // games" section: the gzip + decompressionFallback approach this
        // project used earlier (matching the old, archived project's
        // BuildScript.cs comment) was verified 2026-08-09 to NOT actually
        // work on real Cloudflare Pages hosting — Cloudflare's edge
        // compression layer strips/overrides a manually-declared
        // Content-Encoding on precompressed files, a documented Cloudflare
        // limitation. Shipping raw instead lets Cloudflare's own automatic
        // compression handle the wire transfer correctly with zero header
        // rules needed. Also separately confirmed this session: this
        // Unity version's loader.js has no client-side JS gunzip fallback
        // at all — decompressionFallback is a dead config key here, it's
        // not referenced anywhere in the generated loader.js.
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/AflField.unity" },
            locationPathName = outputPath,
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        });

        var summary = report.summary;
        Debug.Log($"NDW_BUILD_RESULT={summary.result} totalSize={summary.totalSize} totalTime={summary.totalTime} errors={summary.totalErrors} warnings={summary.totalWarnings}");

        if (summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            EditorApplication.Exit(1);
        }
    }

    // Extracted (2026-08-11) so an automated playtest can construct the
    // exact same scene the real build ships without paying for a full
    // WebGL compile every time — see PlaytestRunner.cs. saveToDisk is
    // false for that path; PerformWebGLBuild always saves since the real
    // build reads the scene back off disk via EditorBuildSettings.
    public static void BuildSceneContents(bool saveToDisk)
    {
        // Real trademark fix (2026-08-10, direct request): "AFL" is the
        // Australian Football League's trademarked name — same category of
        // risk as the earlier "Chaotic Sports" rename away from "Olympics".
        // "Footy" is the generic/colloquial term for the sport, not a mark.
        PlayerSettings.productName = "Mount Duneed Cats Footy";
        PlayerSettings.WebGL.template = "PROJECT:Responsive";

        AssetDatabase.Refresh(); // pick up any newly-added model/texture files before the Build* calls load them

        _groundLayer = EnsureLayer("Ground");
        _playerLayer = EnsureLayer("Player");
        _ballLayer = EnsureLayer("Ball");
        // The contest itself is resolved in script via OverlapSphere
        // (AFLBall.ResolveContest), which ignores this matrix entirely —
        // disabling Ball×Player here only stops the PHYSICS ENGINE from
        // also independently colliding the two, which is what caused the
        // held-ball-vs-carrier stutter.
        Physics.IgnoreLayerCollision(_ballLayer, _playerLayer, true);

        // Real fix (2026-08-11, issue #1's "one fact in two places"): this
        // used to sit at Unity's default -9.81 while AFLPlayer fell under
        // its own -24, so a jump could never land where the ball-trajectory
        // prediction said it would. Both now agree at -14.
        Physics.gravity = new Vector3(0f, -14f, 0f);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var centreCircle = BuildField();
        var goalNorth = BuildGoal(new Vector3(0, 0, 20), Quaternion.identity, "GoalNorth", AFLPlayer.Team.Home);
        var goalSouth = BuildGoal(new Vector3(0, 0, -20), Quaternion.Euler(0, 180, 0), "GoalSouth", AFLPlayer.Team.Away);
        var ball = BuildBall();

        // Roster: 3 per side, one mesh per team recoloured. The first
        // player on each side is the designated Ruck — the one who
        // contests the centre throw-up at the start of every chain (see
        // AFLGameManager.FindRuck). The other two are lead/forward options
        // once their team has the ball (AFLBotBrain.LeadForGoal).
        var homeTint = new Color(0.35f, 0.6f, 1f);    // cool blue — Home
        var awayTint = new Color(1f, 0.4f, 0.35f);    // warm red — Away

        var croc = BuildPlayer("HomeCroc1", AFLPlayer.Team.Home, new Vector3(0, 1, -5),
            "Assets/Models/CrocModel3DAI/CrocModel.glb", isUser: true, attackingGoal: goalNorth, isRuck: true, tint: homeTint);
        BuildPlayer("HomeCroc2", AFLPlayer.Team.Home, new Vector3(-6, 1, -8),
            "Assets/Models/CrocModel3DAI/CrocModel.glb", isUser: false, attackingGoal: goalNorth, isRuck: false, tint: homeTint);
        BuildPlayer("HomeCroc3", AFLPlayer.Team.Home, new Vector3(6, 1, -9),
            "Assets/Models/CrocModel3DAI/CrocModel.glb", isUser: false, attackingGoal: goalNorth, isRuck: false, tint: homeTint);
        BuildPlayer("AwayRoo1", AFLPlayer.Team.Away, new Vector3(0, 1, 5),
            "Assets/Models/RooModel3DAI/RooModel.glb", isUser: false, attackingGoal: goalSouth, isRuck: true, tint: awayTint);
        BuildPlayer("AwayRoo2", AFLPlayer.Team.Away, new Vector3(-6, 1, 8),
            "Assets/Models/RooModel3DAI/RooModel.glb", isUser: false, attackingGoal: goalSouth, isRuck: false, tint: awayTint);
        BuildPlayer("AwayRoo3", AFLPlayer.Team.Away, new Vector3(6, 1, 9),
            "Assets/Models/RooModel3DAI/RooModel.glb", isUser: false, attackingGoal: goalSouth, isRuck: false, tint: awayTint);

        var cam = BuildCamera(croc.transform);
        BuildManagers(ball, cam, centreCircle, goalNorth, goalSouth);
        BuildLighting();

        // Name must stay "TouchBridge" — the HTML control bar targets it by
        // name via unityInstance.SendMessage('TouchBridge', ...).
        var touchBridgeGo = new GameObject("TouchBridge");
        touchBridgeGo.AddComponent<AFLTouchBridge>();

        if (saveToDisk)
        {
            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/AflField.unity");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/AflField.unity", true) };
        }
    }

    static Transform BuildField()
    {
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Field";
        ground.layer = _groundLayer;
        ground.transform.localScale = new Vector3(3.5f, 1, 4.5f); // ~35x45 unit footprint, same scale proven in the old project
        ground.GetComponent<Renderer>().sharedMaterial = SolidColorMaterial(new Color(0.25f, 0.55f, 0.2f));

        var centerCircle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        centerCircle.name = "CenterCircle";
        centerCircle.layer = _groundLayer;
        Object.DestroyImmediate(centerCircle.GetComponent<Collider>());
        centerCircle.transform.position = new Vector3(0, 0.01f, 0);
        centerCircle.transform.localScale = new Vector3(6f, 0.005f, 6f);
        centerCircle.GetComponent<Renderer>().sharedMaterial = SolidColorMaterial(new Color(0.32f, 0.62f, 0.27f));

        return centerCircle.transform;
    }

    // Real posts + three non-overlapping AFLScoringZone triggers per end:
    // one Goal zone between the two tall posts, and two narrower Behind
    // zones flanking it (one per side, out to the short posts) — kept
    // non-overlapping so a single pass through the goalmouth only ever
    // fires one trigger, unlike the old GoalDetector pair which relied on
    // isGoal-priority logic layered on top of overlapping triggers.
    static Transform BuildGoal(Vector3 center, Quaternion rot, string name, AFLPlayer.Team scoringTeam)
    {
        var goalRoot = new GameObject(name);
        goalRoot.transform.position = center;
        goalRoot.transform.rotation = rot;

        CreatePost(goalRoot.transform, new Vector3(-3.2f, 0, 0), 6f, "GoalPostL");
        CreatePost(goalRoot.transform, new Vector3(3.2f, 0, 0), 6f, "GoalPostR");
        CreatePost(goalRoot.transform, new Vector3(-6.4f, 0, 0), 3.5f, "BehindPostL");
        CreatePost(goalRoot.transform, new Vector3(6.4f, 0, 0), 3.5f, "BehindPostR");

        // Real fix (2026-08-11, issue #1): these used to be 4 units tall
        // against 6-unit posts, so a lofted kick could sail clean over the
        // trigger and score nothing. Now comfortably taller than the posts
        // themselves — kick speeds are also lower post-rebuild (max ~17.5
        // m/s vs the old 29), so trajectories are less extreme to begin
        // with, but the trigger shouldn't rely on that alone.
        CreateScoringZone(goalRoot.transform, new Vector3(0, 4f, 0), new Vector3(6.4f, 9f, 2f),
            AFLScoringZone.ScoreType.Goal, scoringTeam, "GoalZone");
        CreateScoringZone(goalRoot.transform, new Vector3(-4.8f, 3.75f, 0), new Vector3(3.2f, 7.5f, 2f),
            AFLScoringZone.ScoreType.Behind, scoringTeam, "BehindZoneL");
        CreateScoringZone(goalRoot.transform, new Vector3(4.8f, 3.75f, 0), new Vector3(3.2f, 7.5f, 2f),
            AFLScoringZone.ScoreType.Behind, scoringTeam, "BehindZoneR");

        return goalRoot.transform;
    }

    static void CreateScoringZone(Transform parent, Vector3 localPos, Vector3 size,
        AFLScoringZone.ScoreType type, AFLPlayer.Team scoringTeam, string name)
    {
        var zone = new GameObject(name);
        zone.transform.SetParent(parent);
        zone.layer = _groundLayer; // scenery — needs to stay on Ball's collidable set, not excluded by anything
        zone.transform.localPosition = localPos;
        var col = zone.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = size;
        var comp = zone.AddComponent<AFLScoringZone>();
        comp.type = type;
        comp.scoringTeam = scoringTeam;
    }

    static void CreatePost(Transform parent, Vector3 localPos, float height, string name)
    {
        var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        post.name = name;
        post.layer = _groundLayer;
        post.transform.SetParent(parent);
        post.transform.localPosition = localPos + Vector3.up * (height / 2f);
        post.transform.localScale = new Vector3(0.25f, height / 2f, 0.25f);
        post.GetComponent<Renderer>().sharedMaterial = SolidColorMaterial(Color.white);
    }

    static AFLBall BuildBall()
    {
        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "Ball";
        ball.layer = _ballLayer;
        ball.transform.position = new Vector3(0, 1.2f, 0);
        ball.transform.localScale = new Vector3(0.4f, 0.28f, 0.4f); // oval-ish AFL ball silhouette
        ball.GetComponent<Renderer>().sharedMaterial = SolidColorMaterial(new Color(0.6f, 0.35f, 0.15f));

        // A small capsule (oriented along Z, the ball's long axis once its
        // transform is scaled) reads closer to a real football's shape
        // than the default SphereCollider CreatePrimitive gives you.
        Object.DestroyImmediate(ball.GetComponent<SphereCollider>());
        var capsule = ball.AddComponent<CapsuleCollider>();
        capsule.direction = 2; // Z-axis
        capsule.radius = 0.5f;
        capsule.height = 1.2f;

        var aflBall = ball.AddComponent<AFLBall>();
        // AFLBall.groundMask defaults to ~0 (everything) via its own field
        // initializer, and playerMask has no default at all — both get
        // narrowed/set explicitly in BuildManagers once all three layers
        // are known to exist.
        return aflBall;
    }

    // Real character models replaced with primitives (2026-08-11, issue #1
    // play-report thread) — the six Meshy/3D-AI-Studio GLBs turned out to
    // have no skeleton, no animation clips, and are each a single welded
    // mesh with limbs/tail floating disconnected from the body at the
    // source-geometry level (confirmed directly in the Editor: every model
    // imports as one MeshRenderer, zero bones, zero clips). No Unity
    // setting or C# fixes that — it needs the source models regenerated or
    // externally rigged, which is a real, separate, art-side task. Rather
    // than let broken art keep blocking every gameplay test (which is what
    // happened for two days), swap to simple readable primitives now:
    // capsule body, sphere head, a small dark "nose" so facing is never
    // ambiguous, solid team colour. This is a straight drop-in swap point
    // — BuildPlayer still passes a model path, just unused for now — real
    // rigged models come back in later by restoring the GLB-loading body
    // this replaced (see git history on this file) once that work is done.
    static void BuildCharacterModel3D(Transform parent, string modelPath, Color? tint)
    {
        _ = modelPath; // kept as a swap point for real rigged models later
        Color c = tint ?? Color.white;
        var mat = SolidColorMaterial(c);

        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Visual";
        body.transform.SetParent(parent, false);
        body.transform.localPosition = new Vector3(0f, 1f, 0f);   // matches the CharacterController's own center
        body.transform.localScale = new Vector3(0.7f, 1f, 0.7f);  // capsule default height 2, radius 0.5
        body.GetComponent<Renderer>().sharedMaterial = mat;
        Object.DestroyImmediate(body.GetComponent<Collider>());

        var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "Head";
        head.transform.SetParent(body.transform, false);
        // Local to the (0.7,1,0.7)-scaled capsule — divide out the parent
        // scale so the head reads as a normal round sphere, not squashed.
        head.transform.localScale = new Vector3(0.5f / 0.7f, 0.5f, 0.5f / 0.7f);
        head.transform.localPosition = new Vector3(0f, 0.62f, 0f);
        head.GetComponent<Renderer>().sharedMaterial = mat;
        Object.DestroyImmediate(head.GetComponent<Collider>());

        // Small dark marker on the front of the head — the one thing every
        // play report this project has ever had in common is not being
        // able to tell which way a character is facing at a glance.
        var nose = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        nose.name = "FacingMarker";
        nose.transform.SetParent(head.transform, false);
        nose.transform.localScale = Vector3.one * 0.32f;
        nose.transform.localPosition = new Vector3(0f, 0f, 0.55f);
        nose.GetComponent<Renderer>().sharedMaterial = SolidColorMaterial(new Color(0.08f, 0.08f, 0.08f));
        Object.DestroyImmediate(nose.GetComponent<Collider>());

        // No custom hands/ballHold anchors here — AFLPlayer's own defaults
        // (standingReach 2.20, ballHold at (0.35,1.15,0.35)) were already
        // tuned for a roughly-2-unit-tall, 1x-scale body, which is exactly
        // what this primitive is. The bounds-derived anchor code this
        // replaced existed specifically to compensate for the broken GLBs'
        // unpredictable proportions — not needed for known-good geometry.
    }

    static Transform CreateAnchor(Transform parent, string n, Vector3 local)
    {
        var t = new GameObject(n).transform;
        t.SetParent(parent, false);
        t.localPosition = local;
        return t;
    }

    static AFLPlayer BuildPlayer(string name, AFLPlayer.Team team, Vector3 position,
        string modelPath, bool isUser, Transform attackingGoal, bool isRuck, Color? tint = null)
    {
        var go = new GameObject(name);
        go.layer = _playerLayer;
        go.transform.position = position;

        var cc = go.AddComponent<CharacterController>();
        cc.height = 2f;
        cc.center = new Vector3(0, 1f, 0);

        var p = go.AddComponent<AFLPlayer>();
        p.team = team;
        p.isUserControlled = isUser;
        p.isRuck = isRuck;
        // Faced toward their own attacking end from spawn — matches
        // whichever goal BuildManagers/AFLBotBrain will send them toward.
        Vector3 faceDir = team == AFLPlayer.Team.Home ? Vector3.forward : Vector3.back;
        go.transform.rotation = Quaternion.LookRotation(faceDir, Vector3.up);

        BuildCharacterModel3D(go.transform, modelPath, tint);

        // Every player gets a brain now, including the human-designated
        // one — isUserControlled is the only gate AFLBotBrain/AFLPlayer
        // check, so control handing away from someone mid-chain no longer
        // leaves them standing frozen. Issue #1: "HomeCroc1 built with no
        // AFLBotBrain... permanently frozen once control leaves it."
        var brain = go.AddComponent<AFLBotBrain>();
        brain.attackingGoal = attackingGoal;
        brain.skill = 0.28f;

        return p;
    }

    static AFLBroadcastCamera BuildCamera(Transform followTarget)
    {
        var cameraGo = new GameObject("Main Camera");
        var cam = cameraGo.AddComponent<Camera>();
        cam.tag = "MainCamera";
        cam.backgroundColor = new Color(0.55f, 0.75f, 0.95f);
        cam.clearFlags = CameraClearFlags.SolidColor;

        var rig = cameraGo.AddComponent<AFLBroadcastCamera>();
        rig.target = followTarget;
        // Ground AND Player (2026-08-11 fix — was Ground-only, which let
        // the camera clip straight through any OTHER player during a
        // contest/centre bounce; see the matching comment in
        // AFLBroadcastCamera.LateUpdate() for why). The followed player's
        // own CharacterController is explicitly excluded in code there via
        // a target-root check, so this doesn't reintroduce the old
        // "camera jams into its own target's back" bug.
        rig.collisionMask = (1 << _groundLayer) | (1 << _playerLayer);
        // Matches AFLBroadcastCamera's own distance/height defaults so
        // there's no jarring snap on the very first frame before
        // SmoothDamp converges.
        cameraGo.transform.position = followTarget.position + new Vector3(0, 1.9f, -6f);

        return rig;
    }

    static void BuildManagers(AFLBall ball, AFLBroadcastCamera cam, Transform centreCircle, Transform goalNorth, Transform goalSouth)
    {
        var gm = new GameObject("GameManager");
        var manager = gm.AddComponent<AFLGameManager>();
        manager.ball = ball;
        manager.cam = cam;
        manager.centreCircle = centreCircle;
        manager.goalNorth = goalNorth;
        manager.goalSouth = goalSouth;
        manager.userTeam = AFLPlayer.Team.Home;

        // AFLBall.playerMask has no field-initializer default (unlike
        // groundMask, which does) — both narrowed to the real dedicated
        // layers now that they exist, rather than the old single-Default
        // shortcut.
        ball.playerMask = 1 << _playerLayer;
        ball.groundMask = 1 << _groundLayer;
    }

    // Standard pattern for programmatically registering a Tags & Layers
    // entry — layers 0-7 are Unity's reserved built-ins, user layers start
    // at 8. Reuses an existing slot with a matching name if this method
    // has already run once for the same project (idempotent across
    // repeated builds).
    static int EnsureLayer(string name)
    {
        var tagManagerAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        var tagManager = new SerializedObject(tagManagerAssets[0]);
        var layersProp = tagManager.FindProperty("layers");

        for (int i = 8; i < layersProp.arraySize; i++)
        {
            if (layersProp.GetArrayElementAtIndex(i).stringValue == name) return i;
        }
        for (int i = 8; i < layersProp.arraySize; i++)
        {
            var sp = layersProp.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(sp.stringValue))
            {
                sp.stringValue = name;
                tagManager.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
                return i;
            }
        }
        throw new System.Exception("No free layer slots available for '" + name + "'");
    }

    static void BuildLighting()
    {
        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        lightGo.transform.rotation = Quaternion.Euler(55, -30, 0);
    }

    static Material SolidColorMaterial(Color color)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        mat.color = color;
        return mat;
    }
}
