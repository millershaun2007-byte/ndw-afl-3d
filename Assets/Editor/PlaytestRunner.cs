using UnityEngine;
using UnityEditor;

// Invoked headlessly via:
//   Unity -batchmode -projectPath . -executeMethod PlaytestRunner.RunSimulatedPlaytest -logFile ...
// Deliberately no -quit on that command line — PlaytestDriver.Finish() is
// what actually calls EditorApplication.Exit() once the simulated run
// settles on a result. Builds the same scene BuildScript.PerformWebGLBuild
// would (via the shared BuildSceneContents), skips the disk-save/WebGL
// compile since this only needs to run the actual gameplay logic in Play
// Mode, and drops a PlaytestDriver into the scene before entering it.
public static class PlaytestRunner
{
    public static void RunSimulatedPlaytest()
    {
        BuildScript.BuildSceneContents(saveToDisk: false);

        var driverGo = new GameObject("PlaytestDriver");
        driverGo.AddComponent<PlaytestDriver>();

        EditorApplication.isPlaying = true;
    }
}
