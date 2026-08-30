using UnityEngine;

namespace AFL.Day1
{
    // The app-bridge tap receiver, called by name from the page wrapper
    // (SendMessage to "TouchBridge" / "TapPressed").
    //
    // 2026-08-31 — MOVED OUT of Day1RuckContest.cs, where it had lived since
    // it was written. Unity can only resolve a MonoBehaviour to a script asset
    // when its class sits in a file of the same name, so while it lived in
    // that file the AddComponent<Day1TouchBridge>() in MainBuildScript
    // produced a component the scene could not serialise — the built scene
    // ended up with a bare "TouchBridge" GameObject and no script on it at
    // all, which is why the bridge tap has never actually arrived in a built
    // game. Spacebar and mouse still worked (Day1Input.TapDown reads those
    // directly), which is why it went unnoticed.
    public class Day1TouchBridge : MonoBehaviour
    {
        void LateUpdate() { Day1Input.ClearOneShot(); }
        public void TapPressed(string _) { Day1Input.TouchTapDown = true; }
    }
}
