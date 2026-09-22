using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// Renders the built scene from its own Main Camera, to see what the player sees.
public static class CaptureGameView
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/AflMatch.unity", OpenSceneMode.Single);
        var cam = Object.FindFirstObjectByType<Camera>();
        if (!cam) { Debug.Log("GAMEVIEW: no camera"); return; }
        Debug.Log($"GAMEVIEW: cam pos={cam.transform.position} rot={cam.transform.eulerAngles} fov={cam.fieldOfView}");

        foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
        {
            if (go.name != "GoalPost") continue;
            var vp = cam.WorldToViewportPoint(go.transform.position);
            bool onScreen = vp.z > 0 && vp.x > 0 && vp.x < 1 && vp.y > 0 && vp.y < 1;
            Debug.Log($"GAMEVIEW: post at z={go.transform.position.z:F1} viewport=({vp.x:F2},{vp.y:F2},{vp.z:F1}) onScreen={onScreen}");
        }

        var rt = new RenderTexture(960, 600, 24);
        cam.targetTexture = rt; cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(960, 600, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 960, 600), 0, 0); tex.Apply();
        System.IO.File.WriteAllBytes("/tmp/gameview.png", tex.EncodeToPNG());
        RenderTexture.active = null; cam.targetTexture = null;
        Debug.Log("GAMEVIEW: wrote /tmp/gameview.png");
    }
}
