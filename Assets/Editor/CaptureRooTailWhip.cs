using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;

// Real Tail-Whip pose for Roo vs Croc's Tail-Whip mechanic (2026-08-22),
// posed on the real Meshy-rigged Roo model (RooRigged.glb) rather than a
// hand-drawn/procedural placeholder — same "real character art" pipeline
// as CaptureAllPortraits.cs.
//
// Important, honest limitation (checked directly, not assumed): dumping
// Roo's bone hierarchy (DumpRooBones.cs) confirmed this rig has NO tail
// bone at all — it's the same standard bipedal skeleton as every other
// character in this library (Hips/Spine/legs/arms/neck/head), and the
// auto-rig API's pose estimation only ever produces that set. So this
// isn't a literal articulated tail swing — it's a dynamic full-body
// spin/wind-up stance (hip+spine twist, wide braced stance, arms swept
// for follow-through) using the bones that actually exist, which is how
// many games depict a "tail whip" special move regardless of whether the
// tail itself is separately rigged. Worth documenting so nobody assumes a
// tail-bone animation was captured when it wasn't.
public static class CaptureRooTailWhip
{
    public static void Capture()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Single);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/RooRiggedAI/RooRigged.glb");
        if (!prefab) { Debug.LogError("NDW_WHIP_FAIL: could not load RooRigged.glb"); return; }
        var instance = Object.Instantiate(prefab);
        instance.name = "Roo";
        instance.transform.position = Vector3.zero;
        foreach (var col in instance.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);

        PoseTailWhip(instance.transform);

        // Same camera/lighting recipe as CaptureAllPortraits.RenderTransparent,
        // but a slightly tighter/lower angle (yaw 160, closer) so the wide
        // braced stance doesn't clip out of frame the way the default
        // standing-portrait framing would.
        RenderTransparent(instance, Vector3.zero, 160f, "Logs/roo-tailwhip/roo-tailwhip.png");

        Object.DestroyImmediate(instance);
        Debug.Log("NDW_WHIP_DONE");
    }

    static Transform Find(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>()) if (t.name == name) return t;
        return null;
    }

    static void PoseTailWhip(Transform root)
    {
        var hips = Find(root, "Hips");
        var spine02 = Find(root, "Spine02"); // closest to hips in this rig's reversed spine chain
        var spine01 = Find(root, "Spine01");
        var spine = Find(root, "Spine");     // closest to shoulders
        var leftUpLeg = Find(root, "LeftUpLeg");
        var rightUpLeg = Find(root, "RightUpLeg");
        var leftArm = Find(root, "LeftArm");
        var rightArm = Find(root, "RightArm");
        var leftForeArm = Find(root, "LeftForeArm");
        var rightForeArm = Find(root, "RightForeArm");
        var head = Find(root, "Head");

        // Hips: braced, wide pivot stance, twisted into the whip.
        if (hips) hips.localRotation *= Quaternion.Euler(-6f, 28f, 4f);
        // Spine: counter-rotates through the chain for a real wind-up/
        // release silhouette rather than a rigid twisted plank.
        if (spine02) spine02.localRotation *= Quaternion.Euler(4f, -10f, -6f);
        if (spine01) spine01.localRotation *= Quaternion.Euler(2f, -8f, -4f);
        if (spine) spine.localRotation *= Quaternion.Euler(-2f, -6f, -3f);
        // Legs: wide braced stance, one forward one back, like the moment
        // just after a pivoting whip motion.
        if (leftUpLeg) leftUpLeg.localRotation *= Quaternion.Euler(-18f, 8f, 10f);
        if (rightUpLeg) rightUpLeg.localRotation *= Quaternion.Euler(22f, -6f, -6f);
        // Arms swept out and back for follow-through/balance.
        if (leftArm) leftArm.localRotation *= Quaternion.Euler(-20f, 0f, 55f);
        if (rightArm) rightArm.localRotation *= Quaternion.Euler(30f, 10f, -40f);
        if (leftForeArm) leftForeArm.localRotation *= Quaternion.Euler(0f, 0f, 25f);
        if (rightForeArm) rightForeArm.localRotation *= Quaternion.Euler(0f, 0f, -15f);
        // Head glances toward the whip direction.
        if (head) head.localRotation *= Quaternion.Euler(0f, -12f, 0f);
    }

    static void RenderTransparent(GameObject instance, Vector3 pos, float yawDeg, string outPng)
    {
        var camGo = new GameObject("Cam_tmp");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0, 0, 0, 0);
        camGo.transform.position = pos + Quaternion.Euler(0, yawDeg, 0) * new Vector3(0, 1.2f, -2.3f);
        camGo.transform.LookAt(pos + new Vector3(0, 0.95f, 0));

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
