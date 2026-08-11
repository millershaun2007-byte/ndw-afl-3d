using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Collections.Generic;

// 2026-08-11 — the decisive test CLAUDE.md has been flagging as
// "genuinely untested" since the character-library investigation: does a
// real animation clip actually retarget and LOOK correct once a proper
// Humanoid avatar exists, or does the structural pass (isValid/isHuman)
// just mean the check is satisfied without the result looking right (the
// Octopus lesson). Renders Croc and Roo mid-stride on the real *Walking
// clips using the same corrected bone mapping BuildScript.cs uses for the
// six-player game — duplicated here rather than making that method public,
// so this stays a read-only test against the old game's code, not a change
// to it.
public static class AvatarRetargetTest
{
    static readonly Dictionary<string, HumanBodyBones> HumanoidBoneMap = new()
    {
        { "Hips", HumanBodyBones.Hips },
        { "Spine02", HumanBodyBones.Spine },
        { "Spine01", HumanBodyBones.Chest },
        { "Spine", HumanBodyBones.UpperChest },
        { "neck", HumanBodyBones.Neck },
        { "Head", HumanBodyBones.Head },
        { "LeftShoulder", HumanBodyBones.LeftShoulder },
        { "LeftArm", HumanBodyBones.LeftUpperArm },
        { "LeftForeArm", HumanBodyBones.LeftLowerArm },
        { "LeftHand", HumanBodyBones.LeftHand },
        { "RightShoulder", HumanBodyBones.RightShoulder },
        { "RightArm", HumanBodyBones.RightUpperArm },
        { "RightForeArm", HumanBodyBones.RightLowerArm },
        { "RightHand", HumanBodyBones.RightHand },
        { "LeftUpLeg", HumanBodyBones.LeftUpperLeg },
        { "LeftLeg", HumanBodyBones.LeftLowerLeg },
        { "LeftFoot", HumanBodyBones.LeftFoot },
        { "LeftToeBase", HumanBodyBones.LeftToes },
        { "RightUpLeg", HumanBodyBones.RightUpperLeg },
        { "RightLeg", HumanBodyBones.RightLowerLeg },
        { "RightFoot", HumanBodyBones.RightFoot },
        { "RightToeBase", HumanBodyBones.RightToes },
    };

    public static void Run()
    {
        EditorSceneManager_NewScene();

        DumpSkinning("Croc", "Assets/Models/CrocRiggedAI/CrocRigged.glb");
        RenderRawBindPose("Croc", "Assets/Models/CrocRiggedAI/CrocRigged.glb", new Vector3(0, 0, 0), Quaternion.Euler(0, 200, 0), "Logs/retarget-croc-RAWBIND.png");
        RenderGenericNoAvatar("Croc", "Assets/Models/CrocRiggedAI/CrocRigged.glb", "Assets/Models/CrocRiggedAI/CrocWalking.glb",
            new Vector3(0, 0, 0), Quaternion.Euler(0, 200, 0), "Logs/retarget-croc-GENERIC.png");
        RenderGenericNoAvatar("Roo", "Assets/Models/RooRiggedAI/RooRigged.glb", "Assets/Models/RooRiggedAI/RooWalking.glb",
            new Vector3(0, 0, 0), Quaternion.Euler(0, 200, 0), "Logs/retarget-roo-GENERIC.png");

        RenderOne("Croc", "Assets/Models/CrocRiggedAI/CrocRigged.glb", "Assets/Models/CrocRiggedAI/CrocWalking.glb",
            new Vector3(0, 0, 0), Quaternion.Euler(0, 200, 0), "Logs/retarget-croc.png");
        RenderOne("Roo", "Assets/Models/RooRiggedAI/RooRigged.glb", "Assets/Models/RooRiggedAI/RooWalking.glb",
            new Vector3(0, 0, 0), Quaternion.Euler(0, 200, 0), "Logs/retarget-roo.png");

        Debug.Log("NDW_RETARGET_TEST_DONE");
    }

    static void DumpSkinning(string label, string riggedPath)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(riggedPath);
        var instance = Object.Instantiate(prefab);
        var smrs = instance.GetComponentsInChildren<SkinnedMeshRenderer>();
        Debug.Log($"NDW_SKIN_DUMP {label}: {smrs.Length} SkinnedMeshRenderer(s)");
        foreach (var smr in smrs)
        {
            var boneNames = smr.bones.Select(b => b ? b.name : "NULL").ToArray();
            Debug.Log($"NDW_SKIN_DUMP {label}.{smr.name}: rootBone={(smr.rootBone ? smr.rootBone.name : "NULL")} bones=[{string.Join(",", boneNames)}] containsLeftArm={boneNames.Contains("LeftArm")} containsRightArm={boneNames.Contains("RightArm")}");
        }
        var allNames = instance.GetComponentsInChildren<Transform>().Select(t => t.name).ToArray();
        Debug.Log($"NDW_SKIN_DUMP {label}: all transform names = [{string.Join(",", allNames)}]");
        Object.DestroyImmediate(instance);
    }

    static void EditorSceneManager_NewScene()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Single);
    }

    static void RenderOne(string label, string riggedPath, string walkPath, Vector3 pos, Quaternion rot, string outPng)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(riggedPath);
        if (!prefab) { Debug.LogError($"NDW_RETARGET_TEST_FAIL {label}: could not load {riggedPath}"); return; }
        var instance = Object.Instantiate(prefab);
        instance.name = label;
        instance.transform.position = pos;
        instance.transform.rotation = rot;
        foreach (var col in instance.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
        foreach (var anim in instance.GetComponentsInChildren<Animator>()) Object.DestroyImmediate(anim);
        // updateWhenOffscreen defaults to false — SkinnedMeshRenderer skips
        // recomputing skin deformation using bounds cached from the ORIGINAL
        // bind pose, which don't get refreshed synchronously after a script-
        // driven pose change with no player-loop tick in between (batchmode
        // Edit mode, not Play mode). Confirmed this was the actual bug, not
        // the T-pose math: logged LeftArm's rotation immediately before each
        // render and it WAS changing between attempts while the rendered
        // pixels stayed pixel-identical.
        foreach (var smr in instance.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.updateWhenOffscreen = true;

        // Real fix, second attempt: editing HumanDescription.skeleton's
        // rotation values alone did nothing (confirmed — the first
        // T-pose render came out identical to the broken one).
        // AvatarBuilder.BuildHumanAvatar reads the LIVE transform
        // hierarchy of `instance` as its T-pose reference, not the
        // values passed in hd.skeleton — those exist mainly for
        // name/scale bookkeeping. The rig has to actually BE in T-pose
        // at the moment the avatar is built, so apply the correction to
        // the real bone transforms first.
        ApplyTPoseCorrection(instance);
        var avatar = BuildHumanoidAvatar(instance);
        if (avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            Debug.LogError($"NDW_RETARGET_TEST_FAIL {label}: avatar invalid (valid={(avatar ? avatar.isValid : false)} isHuman={(avatar ? avatar.isHuman : false)})");
            return;
        }

        var walkClip = AssetDatabase.LoadAllAssetsAtPath(walkPath).OfType<AnimationClip>().FirstOrDefault();
        if (walkClip == null) { Debug.LogError($"NDW_RETARGET_TEST_FAIL {label}: no AnimationClip found in {walkPath}"); return; }

        var animator = instance.AddComponent<Animator>();
        animator.avatar = avatar;

        // Render the avatar's own declared T-pose FIRST (zero muscles) —
        // this is the direct, literal check of whether the correction
        // above actually produced a sane reference frame, before judging
        // whether a clip retargeted onto it looks right. Read the actual
        // body position/rotation the avatar computes for the pose the
        // rig is already sitting in (just T-pose-corrected above) rather
        // than guessing one — hasTranslationDoF is false, so an invented
        // bodyPosition risks distorting limbs to compensate for a value
        // that was never real to begin with.
        var poseHandler = new HumanPoseHandler(avatar, instance.transform);
        var zeroPose = new HumanPose();
        poseHandler.GetHumanPose(ref zeroPose);
        zeroPose.muscles = new float[HumanTrait.MuscleCount];
        poseHandler.SetHumanPose(ref zeroPose);
        var leftArmCheck = instance.GetComponentsInChildren<Transform>().First(t => t.name == "LeftArm");
        Debug.Log($"NDW_DEBUG {label} LeftArm.localEulerAngles after SetHumanPose = {leftArmCheck.localEulerAngles} (world={leftArmCheck.eulerAngles})");
        RenderCamera(instance, pos, outPng.Replace(".png", "-tpose.png"));

        // Then sample a mid-stride frame directly via AnimationMode — no
        // Play Mode needed, works in -batchmode. This is the actual
        // muscle-space retarget path (Humanoid avatar + clip), not the
        // bone-name Generic matching the six-player game already relies on.
        AnimationMode.StartAnimationMode();
        AnimationMode.SampleAnimationClip(instance, walkClip, walkClip.length * 0.35f);
        RenderCamera(instance, pos, outPng);
        AnimationMode.StopAnimationMode();

        Debug.Log($"NDW_RETARGET_TEST_OK {label} -> {outPng}");

        Object.DestroyImmediate(instance);
    }

    static void RenderRawBindPose(string label, string riggedPath, Vector3 pos, Quaternion rot, string outPng)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(riggedPath);
        var instance = Object.Instantiate(prefab);
        instance.transform.position = pos;
        instance.transform.rotation = rot;
        RenderCamera(instance, pos, outPng);
        Object.DestroyImmediate(instance);
    }

    // No Avatar at all — Generic animation, matching curves to bones by
    // name/path. This is what BuildScript.cs's six-player game already
    // ships with successfully (its own comment: "the walk/run clips still
    // played fine... Generic animation matches by bone name"). The
    // Humanoid-avatar path above produced a malformed zero-pose entirely
    // independent of any clip (confirmed: raw bind pose renders correctly,
    // the avatar's own reference pose does not) — so this sidesteps that
    // problem completely rather than trying to fix it.
    static void RenderGenericNoAvatar(string label, string riggedPath, string walkPath, Vector3 pos, Quaternion rot, string outPng)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(riggedPath);
        var instance = Object.Instantiate(prefab);
        instance.name = label;
        instance.transform.position = pos;
        instance.transform.rotation = rot;
        foreach (var col in instance.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
        foreach (var anim in instance.GetComponentsInChildren<Animator>()) Object.DestroyImmediate(anim);
        foreach (var smr in instance.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.updateWhenOffscreen = true;

        var walkClip = AssetDatabase.LoadAllAssetsAtPath(walkPath).OfType<AnimationClip>().FirstOrDefault();
        if (walkClip == null) { Debug.LogError($"NDW_GENERIC_TEST_FAIL {label}: no AnimationClip found in {walkPath}"); return; }

        AnimationMode.StartAnimationMode();
        AnimationMode.SampleAnimationClip(instance, walkClip, walkClip.length * 0.35f);
        RenderCamera(instance, pos, outPng);
        AnimationMode.StopAnimationMode();

        Debug.Log($"NDW_GENERIC_TEST_OK {label} -> {outPng}");
        Object.DestroyImmediate(instance);
    }

    static void RenderCamera(GameObject instance, Vector3 pos, string outPng)
    {
        var camGo = new GameObject("Cam_tmp");
        var cam = camGo.AddComponent<Camera>();
        cam.backgroundColor = new Color(0.55f, 0.75f, 0.95f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        camGo.transform.position = pos + new Vector3(0, 1.4f, -3.2f);
        camGo.transform.LookAt(pos + new Vector3(0, 1.1f, 0));

        var lightGo = new GameObject("Light_tmp");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        lightGo.transform.rotation = Quaternion.Euler(55, -30, 0);

        // SkinnedMeshRenderer's GPU-skinned buffers don't always reflect a
        // Transform change made moments earlier via script in Edit mode —
        // unlike Play mode, there's no guaranteed player-loop tick between
        // "set the pose" and "render" to force the skin recompute. One
        // throwaway render forces that recompute; the second is the one
        // actually captured.
        int w = 800, h = 600;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt;
        cam.Render();
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        cam.targetTexture = null;
        rt.Release();

        Directory.CreateDirectory(Path.GetDirectoryName(outPng));
        File.WriteAllBytes(outPng, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(camGo);
        Object.DestroyImmediate(lightGo);
    }

    // Real fix (2026-08-11, Shaun playtest of the first retarget render:
    // arms clenched up near the jaw instead of a walking stride). Root
    // cause confirmed by pulling the raw GLB node rotations directly:
    // LeftArm/RightArm sit at non-identity quaternions in the bind pose —
    // this rig's rest pose already has the arms bent down at the sides,
    // not held out horizontally. Unity's Humanoid muscle system computes
    // every clip's retarget relative to the T-pose declared in
    // HumanDescription.skeleton; feeding it this rig's actual (non-T)
    // rest rotation for the arm bones is exactly the mismatch that
    // produces this kind of "everything clamps toward the body" artifact.
    // The correct fix isn't to alter the live rig at all — it's to
    // declare a proper T-pose (arms held straight out to the sides) in
    // the skeleton array Unity uses purely as its muscle reference frame,
    // independent of whatever pose the actual GameObject sits in at
    // runtime. Derived the correction empirically (same technique used to
    // find the 140-degree overhead-raise rotation in Day1RuckContest):
    // local Z axis is this rig's shoulder-abduction axis, so a T-pose is
    // a fixed rotation on that same axis away from the captured rest
    // value, mirrored per side.
    static readonly Dictionary<string, float> TPoseZCorrectionDegrees = new()
    {
        { "LeftArm", 78f },
        { "RightArm", -78f },
        { "LeftForeArm", 8f },
        { "RightForeArm", -8f },
    };

    static void ApplyTPoseCorrection(GameObject instance)
    {
        var boneByName = instance.GetComponentsInChildren<Transform>().ToDictionary(t => t.name, t => t);
        foreach (var kv in TPoseZCorrectionDegrees)
        {
            if (boneByName.TryGetValue(kv.Key, out var bone))
                bone.localRotation = bone.localRotation * Quaternion.Euler(0, 0, kv.Value);
        }
    }

    static Avatar BuildHumanoidAvatar(GameObject instance)
    {
        var boneByName = instance.GetComponentsInChildren<Transform>().ToDictionary(t => t.name, t => t);

        var boneList = new List<HumanBone>();
        foreach (var kv in HumanoidBoneMap)
        {
            if (!boneByName.ContainsKey(kv.Key)) continue;
            boneList.Add(new HumanBone { boneName = kv.Key, humanName = kv.Value.ToString(), limit = new HumanLimit { useDefaultValues = true } });
        }

        var hd = new HumanDescription
        {
            human = boneList.ToArray(),
            skeleton = instance.GetComponentsInChildren<Transform>().Select(t =>
                new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }
            ).ToArray(),
            upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
            armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f, hasTranslationDoF = false,
        };

        Avatar avatar = null;
        try { avatar = AvatarBuilder.BuildHumanAvatar(instance, hd); }
        catch (System.Exception e) { Debug.LogWarning($"BuildHumanoidAvatar failed for {instance.name}: {e.Message}"); }
        return avatar;
    }
}
