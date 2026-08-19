using UnityEngine;
using UnityEditor;

public static class MeasureMiaSummer
{
    public static void Run()
    {
        Measure("Mia", "Assets/Models/MiaRiggedAI/MiaRigged.glb");
        Measure("Summer", "Assets/Models/SummerRiggedAI/SummerRigged.glb");
    }

    static void Measure(string name, string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!prefab) { Debug.LogError($"MEASURE {name}: could not load {path}"); return; }
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        var renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) { Debug.LogError($"MEASURE {name}: no renderers"); Object.DestroyImmediate(instance); return; }
        var bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        Debug.Log($"MEASURE {name}: height={bounds.size.y} width={bounds.size.x} depth={bounds.size.z}");
        Object.DestroyImmediate(instance);
    }
}
