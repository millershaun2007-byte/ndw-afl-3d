using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;

// Batch visual audit (2026-08-18) — Shaun's ask: go through every rigged
// character one by one and actually confirm it looks right, not just that
// it structurally built an avatar. Renders bind pose AND a mid-stride
// Generic-animation walk frame (bone-name matching, no Humanoid avatar
// needed — same approach BuildScript.cs's six-player game already relies
// on successfully, per AvatarRetargetTest.cs's own findings) for every
// character, so real deformation/floating-limb problems are visible, not
// hidden behind a static T-pose.
public static class AuditAllCharacters
{
    static readonly string[] Names = {
        "Buffalo", "Bumblebee", "Cat", "Chicken", "Cow", "Croc", "Dog",
        "Dragon", "Duck", "Elephant", "Emu", "Fairy", "Giant", "Giraffe",
        "Leopard", "Lion", "Mouse", "Octopus", "Rhino", "Roo", "Trex",
        "Unicorn", "Wizard"
    };

    public static void Run()
    {
        foreach (var name in Names)
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            var riggedPath = $"Assets/Models/{name}RiggedAI/{name}Rigged.glb";
            var walkPath = FindWalkPath(name);

            AuditOne(name, riggedPath, walkPath);
        }
        Debug.Log("NDW_AUDIT_ALL_DONE");
    }

    static string FindWalkPath(string name)
    {
        // Walking.glb wasn't copied into the Unity project for most
        // characters (only bind-pose Rigged.glb was) — check the character
        // library directly instead of assuming it's in Assets/.
        var libPath = $"/Users/shaun/Projects/ndw-character-library/models/rigged/{name}Walking.glb";
        return File.Exists(libPath) ? libPath : null;
    }

    static void AuditOne(string label, string riggedPath, string walkLibPath)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(riggedPath);
        if (!prefab) { Debug.LogError($"NDW_AUDIT_FAIL {label}: could not load {riggedPath}"); return; }

        var instance = Object.Instantiate(prefab);
        instance.name = label;
        var pos = Vector3.zero;
        instance.transform.position = pos;
        instance.transform.rotation = Quaternion.Euler(0, 200, 0);

        foreach (var col in instance.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
        foreach (var anim in instance.GetComponentsInChildren<Animator>()) Object.DestroyImmediate(anim);
        foreach (var smr in instance.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.updateWhenOffscreen = true;

        // Bind pose
        RenderCamera(instance, pos, $"Logs/audit/{label}-bindpose.png");

        // Mid-stride Generic-animation frame, if a Walking clip exists in
        // the library (it does for all 23 — copy it in fresh from the repo
        // since most weren't copied into Assets/ yet).
        if (walkLibPath != null)
        {
            var tmpAssetPath = $"Assets/_TmpWalk/{label}Walking.glb";
            Directory.CreateDirectory("Assets/_TmpWalk");
            File.Copy(walkLibPath, Path.Combine(Directory.GetCurrentDirectory(), tmpAssetPath), true);
            AssetDatabase.ImportAsset(tmpAssetPath);
            var walkClip = AssetDatabase.LoadAllAssetsAtPath(tmpAssetPath).OfType<AnimationClip>().FirstOrDefault();
            if (walkClip != null)
            {
                AnimationMode.StartAnimationMode();
                AnimationMode.SampleAnimationClip(instance, walkClip, walkClip.length * 0.35f);
                RenderCamera(instance, pos, $"Logs/audit/{label}-walk.png");
                AnimationMode.StopAnimationMode();
            }
            else
            {
                Debug.LogWarning($"NDW_AUDIT_NOWALKCLIP {label}: {tmpAssetPath} had no AnimationClip");
            }
        }

        Object.DestroyImmediate(instance);
        Debug.Log($"NDW_AUDIT_OK {label}");
    }

    static void RenderCamera(GameObject instance, Vector3 pos, string outPng)
    {
        var camGo = new GameObject("Cam_tmp");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.55f, 0.75f, 0.95f);
        camGo.transform.position = pos + new Vector3(0, 1.4f, -3.2f);
        camGo.transform.LookAt(pos + new Vector3(0, 1.1f, 0));

        var lightGo = new GameObject("Light_tmp");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        lightGo.transform.rotation = Quaternion.Euler(55, -30, 0);

        int w = 640, h = 480;
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
}
