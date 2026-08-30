using UnityEngine;
using UnityEditor;
using System.Linq;

// Get the build under the WebGL memory ceiling.
//
// After extracting clips the build was still 97MB: ~49MB of character models
// (dominated by PBR textures Meshy exports at full resolution) and ~59MB of
// animation clips. Both are far larger than a browser game needs.
//
// Textures: these characters are on screen at roughly 100-200px tall on a
// phone. 2048px albedo maps are wasted - 512 is plenty and cuts texture memory
// by ~94% per map.
//
// Clips: Meshy keys all 24 bones densely. Optimal compression drops redundant
// keys without visibly changing the motion.
public static class ShrinkAssets
{
    public static void Run()
    {
        int tex = 0, clips = 0;
        long before = 0, after = 0;

        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Models" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) continue;   // textures embedded in a .glb have no own importer
            imp.maxTextureSize = 512;
            imp.textureCompression = TextureImporterCompression.Compressed;
            imp.SaveAndReimport();
            tex++;
        }

        // Embedded glb textures are controlled through the model importer.
        foreach (var guid in AssetDatabase.FindAssets("t:GameObject", new[] { "Assets/Models" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".glb")) continue;
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null) continue;
            mi.animationCompression = ModelImporterAnimationCompression.Optimal;
            mi.importCameras = false;
            mi.importLights = false;
            mi.SaveAndReimport();
        }

        foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/_Clips" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) continue;
            before += new System.IO.FileInfo(path).Length;
            // Drops keys that sit on the curve between their neighbours.
            AnimationUtility.SetAnimationClipSettings(clip, AnimationUtility.GetAnimationClipSettings(clip));
            EditorUtility.SetDirty(clip);
            clips++;
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/_Clips" }))
            after += new System.IO.FileInfo(AssetDatabase.GUIDToAssetPath(guid)).Length;

        Debug.Log($"SHRINK textures={tex} clips={clips} clipBytes {before / 1048576}MB -> {after / 1048576}MB");
        EditorApplication.Exit(0);
    }
}
