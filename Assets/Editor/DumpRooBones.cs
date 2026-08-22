using UnityEngine;
using UnityEditor;

// One-off diagnostic — dumps Roo's full bone hierarchy so a real tail-whip
// pose can be authored against actual bone names, not guessed. See
// neurodinoworld's ndw-roo-croc Tail-Whip work (2026-08-22).
public static class DumpRooBones
{
    public static void Dump()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/RooRiggedAI/RooRigged.glb");
        if (!prefab) { Debug.LogError("NDW_BONES_FAIL: could not load RooRigged.glb"); return; }
        var instance = Object.Instantiate(prefab);
        DumpRecursive(instance.transform, 0);
        Object.DestroyImmediate(instance);
        Debug.Log("NDW_BONES_DONE");
    }

    static void DumpRecursive(Transform t, int depth)
    {
        Debug.Log("NDW_BONE " + new string(' ', depth * 2) + t.name);
        for (int i = 0; i < t.childCount; i++) DumpRecursive(t.GetChild(i), depth + 1);
    }
}
