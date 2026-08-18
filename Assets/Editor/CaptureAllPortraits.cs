using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;

// Batch high-quality portrait capture for all 23 rigged characters
// (2026-08-18) — same process as CaptureRexPortrait.cs (transparent
// ARGB32, 1024x1024, 3/4-angle camera), run for every character so the
// whole library has ready-to-use game-art portraits, not just Rex.
// Octopus excluded — not bipedal, no meaningful "standing portrait" pose
// for it (see ndw-character-library README).
public static class CaptureAllPortraits
{
    static readonly string[] Names = {
        "Buffalo", "Bumblebee", "Cat", "Chicken", "Cow", "Croc", "Dog",
        "Dragon", "Duck", "Elephant", "Emu", "Fairy", "Giant", "Giraffe",
        "Leopard", "Lion", "Mouse", "Rhino", "Roo", "Trex",
        "Unicorn", "Wizard"
    };

    public static void Capture()
    {
        foreach (var name in Names)
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            var riggedPath = $"Assets/Models/{name}RiggedAI/{name}Rigged.glb";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(riggedPath);
            if (!prefab) { Debug.LogError($"NDW_PORTRAIT_FAIL {name}: could not load {riggedPath}"); continue; }

            var instance = Object.Instantiate(prefab);
            instance.name = name;
            var pos = Vector3.zero;
            instance.transform.position = pos;

            foreach (var col in instance.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);

            RenderTransparent(instance, pos, 200f, $"Logs/portraits/{name}.png");

            Object.DestroyImmediate(instance);
            Debug.Log($"NDW_PORTRAIT_OK {name}");
        }
        Debug.Log("NDW_PORTRAIT_ALL_DONE");
    }

    static void RenderTransparent(GameObject instance, Vector3 pos, float yawDeg, string outPng)
    {
        var camGo = new GameObject("Cam_tmp");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0, 0, 0, 0);
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
