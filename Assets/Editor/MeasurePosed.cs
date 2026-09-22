using UnityEngine;
using UnityEditor;
using System.Linq;

// Measures each character at the height it actually stands in its idle pose,
// which is what the player sees - a bind-pose bound can read taller than the game does.
public static class MeasurePosed
{
    public static void Run()
    {
        Check("FootyCats", 1f);
        Check("FootyRoo", 1f);
        Check("FootyCroc", 1f);
        Check("FootyMia", 1.284f);
        Check("FootyDragon", 1.083f * 1.3f);
        Check("FootyLion", 1.227f * 1.3f);
    }

    static void Check(string species, float scale)
    {
        string folder = $"Assets/Models/{species}RiggedAI";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{folder}/{species}Rigged.glb");
        if (!prefab) { Debug.Log($"POSED {species}: no model"); return; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.position = Vector3.zero;
        go.transform.localScale = Vector3.one * scale;

        float bind = Height(go);

        string idlePath = System.IO.Directory.GetFiles(folder, "*Idle.anim").FirstOrDefault();
        float posed = bind;
        if (idlePath != null)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(idlePath.Replace('\\', '/'));
            if (clip) { clip.SampleAnimation(go, clip.length * 0.5f); posed = Height(go); }
        }
        Debug.Log($"POSED {species}: scale={scale:F3} bind={bind:F3} idle={posed:F3}");
        Object.DestroyImmediate(go);
    }

    static float Height(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return 0f;
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b.size.y;
    }
}
