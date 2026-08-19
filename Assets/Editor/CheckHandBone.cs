using UnityEngine;
using UnityEditor;

public static class CheckHandBone
{
    public static void Run()
    {
        Check("Croc", "Assets/Models/CrocRiggedAI/CrocRigged.glb");
        Check("Mia", "Assets/Models/MiaRiggedAI/MiaRigged.glb");
        Check("Summer", "Assets/Models/SummerRiggedAI/SummerRigged.glb");
    }

    static void Check(string name, string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!prefab) { Debug.LogError($"HANDCHECK {name}: could not load {path}"); return; }
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        var found = FindDeepChild(instance.transform, "RightHand");
        Debug.Log($"HANDCHECK {name}: RightHand found={found != null}");
        if (!found)
        {
            var allNames = new System.Text.StringBuilder();
            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                if (t.name.ToLower().Contains("hand")) allNames.Append(t.name).Append(", ");
            Debug.Log($"HANDCHECK {name}: hand-like bone names found: {allNames}");
        }
        Object.DestroyImmediate(instance);
    }

    static Transform FindDeepChild(Transform parent, string name)
    {
        foreach (var child in parent.GetComponentsInChildren<Transform>(true))
            if (child.name == name) return child;
        return null;
    }
}
