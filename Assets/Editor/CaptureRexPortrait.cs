using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;

// One-off portrait capture for Rex (2026-08-18), following the documented
// process in neurodinoworld/CLAUDE.md's "Real character art for 2D/Canvas
// games" section: transparent ARGB32 RenderTexture, camera at a flattering
// 3/4 angle, EncodeToPNG. Critically must run WITHOUT -nographics — that
// flag disables the real GPU Camera.Render() this needs and silently
// produces a blank transparent image.
public static class CaptureRexPortrait
{
    public static void Capture()
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Single);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/TrexRiggedAI/TrexRigged.glb");
        if (!prefab) { Debug.LogError("NDW_CAPTURE_FAIL: could not load TrexRigged.glb"); return; }

        var instance = Object.Instantiate(prefab);
        instance.name = "Rex";
        var pos = new Vector3(0, 0, 0);
        instance.transform.position = pos;

        // Bind pose is already a reasonable standing pose for this rig
        // (confirmed via the earlier text-to-3d preview thumbnail) — no
        // animation sampling needed for a static portrait.

        foreach (var col in instance.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);

        // Try a few yaw offsets since models don't share a consistent
        // "front" convention (per the documented process) — captures all,
        // pick the best one manually rather than guessing blind.
        float[] yaws = { 200f, 160f, 220f, 340f };
        foreach (var yaw in yaws)
        {
            RenderTransparent(instance, pos, yaw, $"Logs/rex-portrait-yaw{yaw}.png");
        }

        Object.DestroyImmediate(instance);
        Debug.Log("NDW_CAPTURE_DONE");
    }

    static void RenderTransparent(GameObject instance, Vector3 pos, float yawDeg, string outPng)
    {
        var camGo = new GameObject("Cam_tmp");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0, 0, 0, 0); // transparent
        camGo.transform.position = pos + Quaternion.Euler(0, yawDeg, 0) * new Vector3(0, 1.3f, -2.6f);
        camGo.transform.LookAt(pos + new Vector3(0, 1.0f, 0));

        var lightGo = new GameObject("Light_tmp");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.3f;
        lightGo.transform.rotation = Quaternion.Euler(50, -35, 0);

        var fillGo = new GameObject("Fill_tmp");
        var fill = fillGo.AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.intensity = 0.4f;
        fillGo.transform.rotation = Quaternion.Euler(30, 150, 0);

        int w = 1024, h = 1024;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        // Two renders — SkinnedMeshRenderer buffers don't always reflect
        // a transform/pose change made moments earlier in a single render
        // call during batchmode Edit mode (no guaranteed player-loop tick).
        cam.Render();
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
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
        Object.DestroyImmediate(fillGo);
    }
}
