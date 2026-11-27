using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static class BuildGame
    {
        static readonly string[] RuntimeShaders =
        {
            "Universal Render Pipeline/Lit", "Stabby/Creature", "Stabby/Foliage", "Stabby/GrassBlade",
            "Stabby/Object", "Stabby/FallMist", "Stabby/VertexLit", "Stabby/Water", "Stabby/Sky", "Stabby/Glow"
        };

        static void KeepRuntimeShaders()
        {
            const string dir = "Assets/Resources/BuildShaders";
            System.IO.Directory.CreateDirectory(dir);
            foreach (var name in RuntimeShaders)
            {
                var path = $"{dir}/{name.Replace('/', '_').Replace(' ', '_')}.mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(path) == null)
                    AssetDatabase.CreateAsset(new Material(Shader.Find(name)), path);
            }
            AssetDatabase.SaveAssets();
        }

        public static void Windows()
        {
            KeepRuntimeShaders();
            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Game.unity" },
                locationPathName = "../play/game/Stabby.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[BuildGame] {report.summary.result} {report.summary.totalSize / (1024 * 1024)} MB in {report.summary.totalTime}");
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
