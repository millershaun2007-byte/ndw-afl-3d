// Point Unity's "External Script Editor" at Cursor and regenerate the
// .sln/.csproj files Cursor's C# extension reads. Run from the menu
// (Tools > Cursor > ...) or headless:
//   Unity -batchmode -quit -projectPath <this repo> -executeMethod CursorSetup.Run
// Added 2026-09-13 at Shaun's request ("getting cursor to help with unity
// would be hugely helpful"). The Visual Studio Editor package may not
// recognise Cursor.app as an editor it can generate project files for; if
// it does not, this generates them as if for VS Code (same format - Cursor
// is a VS Code fork) and then points the editor setting at Cursor.
using UnityEditor;
using UnityEngine;
using Unity.CodeEditor;

public static class CursorSetup
{
    const string CursorApp = "/Applications/Cursor.app";
    const string VSCodeApp = "/Applications/Visual Studio Code.app";

    [MenuItem("Tools/Cursor/Set Cursor as script editor + regenerate project files")]
    public static void Run()
    {
        CodeEditor.SetExternalScriptEditor(CursorApp);
        var handler = CodeEditor.CurrentEditor.GetType().FullName;
        Debug.Log("CursorSetup: editor=" + CodeEditor.CurrentEditorInstallation + " handler=" + handler);
        if (handler.Contains("DefaultExternalCodeEditor"))
        {
            Debug.Log("CursorSetup: Cursor not recognised by an IDE package - generating project files via the VS Code path, then re-pointing at Cursor");
            CodeEditor.SetExternalScriptEditor(VSCodeApp);
            Debug.Log("CursorSetup: temp editor=" + CodeEditor.CurrentEditorInstallation + " handler=" + CodeEditor.CurrentEditor.GetType().FullName);
            CodeEditor.CurrentEditor.SyncAll();
            CodeEditor.SetExternalScriptEditor(CursorApp);
        }
        else
        {
            CodeEditor.CurrentEditor.SyncAll();
        }
        Debug.Log("CursorSetup: done; editor=" + CodeEditor.CurrentEditorInstallation);
    }
}
