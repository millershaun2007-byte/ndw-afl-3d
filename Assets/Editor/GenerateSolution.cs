using System.Reflection;
using UnityEditor;

// Regenerates the .csproj/.sln files an external C# IDE/debugger needs.
// Unity only writes these when it syncs with a code editor, which a headless
// batchmode build never does — so a project built only from the command line
// has none at all, and VS Code reports "There were problems loading project".
// Reflection because SyncVS is internal to UnityEditor.
public static class GenerateSolution
{
    public static void Sync()
    {
        var t = typeof(Editor).Assembly.GetType("UnityEditor.SyncVS");
        var m = t?.GetMethod("SyncSolution", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (m == null) { UnityEngine.Debug.LogError("[GenerateSolution] SyncVS.SyncSolution not found"); return; }
        m.Invoke(null, null);
        UnityEngine.Debug.Log("[GenerateSolution] solution written");
    }
}
