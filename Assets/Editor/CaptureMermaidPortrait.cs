using UnityEngine;
using UnityEditor;
using System.IO;

// Marina's portrait (2026-09-04). Every other character portrait comes from
// CaptureAllPortraits.cs, which loads Assets/Models/<Name>RiggedAI/<Name>Rigged.glb
// — and the mermaid has no rigged variant, because her tail defeats Meshy's
// pose-estimation. That is why she was the one buddy with no 3D portrait, and
// so the one buddy who could not have a Veo welcome clip.
//
// Shaun, 2026-09-04: "she was generated in meshy shes just not rigged". Right —
// and a STILL portrait never needed the rig. Rigging only buys animation. So
// this loads the raw models/mermaid.glb from ndw-character-library directly
// and renders it with the identical camera, lights, size and transparent
// ARGB32 target the other 22 used, so her portrait belongs to the same set.
//
// The one intentional difference is framing: the shared rig is aimed at a
// standing biped, and a mermaid's mass sits lower and longer. FitToBounds()
// below pulls the camera back to whatever her actual renderer bounds need
// rather than trusting a distance measured off a pair of legs.
//
//   Unity -batchmode -quit -executeMethod CaptureMermaidPortrait.Capture
//
// DO NOT PASS -nographics. It disables the real GPU rendering Camera.Render()
// needs and silently writes a blank transparent PNG (CLAUDE.md, "Real
// character art for 2D/Canvas games").
public static class CaptureMermaidPortrait
{
    public static void Capture()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Single);

        const string path = "Assets/Models/Mermaid/mermaid.glb";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!prefab) { Debug.LogError($"NDW_PORTRAIT_FAIL Mermaid: could not load {path}"); return; }

        var instance = Object.Instantiate(prefab);
        instance.name = "Mermaid";
        instance.transform.position = Vector3.zero;
        foreach (var col in instance.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);

        // Try a few yaws — Meshy models share no "front" convention, so the
        // flattering angle differs per character and is found by looking, not
        // by assuming (same lesson as the portrait pass in CLAUDE.md).
        foreach (var yaw in new[] { 0f, 90f, 180f, 200f, 270f })
            RenderTransparent(instance, $"Logs/portraits/Mermaid-yaw{yaw:F0}.png", yaw);

        Object.DestroyImmediate(instance);
        Debug.Log("NDW_PORTRAIT_OK Mermaid");
        Debug.Log("NDW_PORTRAIT_ALL_DONE");
    }

    static void RenderTransparent(GameObject instance, string outPng, float yawDeg)
    {
        // Real bounds, not an assumed standing height.
        var rends = instance.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) { Debug.LogError("NDW_PORTRAIT_FAIL Mermaid: no renderers"); return; }
        var b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        var target = b.center;
        var radius = b.extents.magnitude;

        var camGo = new GameObject("Cam_tmp");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0, 0, 0, 0);
        cam.fieldOfView = 40f;
        // Distance that fits the whole model in frame, with a small margin so
        // nothing clips the edge (a cropped tail would read as a broken render).
        var dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.12f;
        camGo.transform.position = target + Quaternion.Euler(0, yawDeg, 0) * new Vector3(0, radius * 0.35f, -dist);
        camGo.transform.LookAt(target);
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = dist * 4f;

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
        cam.Render();
        cam.Render();   // twice, same as the original — first frame can be empty
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
