using UnityEngine;
using UnityEditor;

// Reports each footy model's rendered height, so a character's scale is measured rather than guessed.
public static class MeasureFooty
{
    public static void Run()
    {
        foreach (var n in new[] { "FootyCats", "FootyMia", "FootyCroc", "FootyRoo", "FootyLion", "FootyDragon" })
            Measure(n, $"Assets/Models/{n}RiggedAI/{n}Rigged.glb");
    }

    static void Measure(string name, string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!prefab) { Debug.Log($"MEASURE {name}: could not load {path}"); return; }
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        var renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) { Debug.Log($"MEASURE {name}: no renderers"); Object.DestroyImmediate(instance); return; }
        var bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        Debug.Log($"MEASURE {name}: height={bounds.size.y:F3}");
        Object.DestroyImmediate(instance);
    }
}
