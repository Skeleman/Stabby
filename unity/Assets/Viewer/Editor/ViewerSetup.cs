using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Stabby.Viewer.Editor
{
    public static class ViewerSetup
    {
        const string SettingsDir = "Assets/Viewer/Settings", ScenePath = "Assets/Scenes/Viewer.unity";

        [MenuItem("Stabby/Set Up URP")]
        public static void EnsureUrp()
        {
            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset) return;
            Directory.CreateDirectory(SettingsDir);
            var data = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(data, SettingsDir + "/Renderer.asset");
            var asset = UniversalRenderPipelineAsset.Create(data);
            asset.shadowDistance = 0;
            AssetDatabase.CreateAsset(asset, SettingsDir + "/URP.asset");
            GraphicsSettings.defaultRenderPipeline = asset;
            int current = QualitySettings.GetQualityLevel();
            for (int q = 0; q < QualitySettings.names.Length; q++)
            {
                QualitySettings.SetQualityLevel(q, false);
                QualitySettings.renderPipeline = asset;
            }
            QualitySettings.SetQualityLevel(current, false);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Stabby/Build Viewer Scene")]
        public static WorldView BuildScene()
        {
            EnsureUrp();
            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Viewer/Terrain.mat");
            if (mat == null)
            {
                mat = new Material(Shader.Find("Stabby/VertexLit"));
                AssetDatabase.CreateAsset(mat, "Assets/Viewer/Terrain.mat");
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var view = new GameObject("World").AddComponent<WorldView>();
            view.Material = mat;

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.1f;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.transform.rotation = Quaternion.Euler(38f, 140f, 0);

            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = view.Sky;
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 60000f;
            cam.fieldOfView = 60f;
            cam.transform.SetPositionAndRotation(new Vector3(15000, 9000, -9000), Quaternion.Euler(35f, 0, 0));
            cam.gameObject.AddComponent<FlyCamera>();
            cam.gameObject.AddComponent<ViewerHud>().View = view;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = view.Sky;
            RenderSettings.fogStartDistance = 6000f;
            RenderSettings.fogEndDistance = 55000f;
            RenderSettings.ambientMode = AmbientMode.Flat;

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            return view;
        }

        public static void Capture()
        {
            int code = 0;
            try { CaptureShots(); }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            EditorApplication.Exit(code);
        }

        static void CaptureShots()
        {
            var view = BuildScene();
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-stabbySeed");
            if (at >= 0 && at + 1 < args.Length) view.Seed = int.Parse(args[at + 1]);
            view.BuildBudget = int.MaxValue;
            view.Build();

            var w = view.World;
            int n = w.Config.Size;
            float cm = w.Config.CellMeters, span = n * cm;
            var cam = Camera.main;
            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "out", "unity", $"seed{view.Seed}"));
            Directory.CreateDirectory(outDir);
            var log = new StringBuilder();

            var main = w.Sketch.Landmasses[0];
            var mainPos = new Vector3(main.X * cm, 0, main.Y * cm);
            var river = w.Water.Rivers.OrderByDescending(r => r.Cells.Count).First();
            int ra = river.Cells[river.Cells.Count / 4], rb = river.Cells[river.Cells.Count * 3 / 4];
            Vector3 Cell(int c) => new Vector3(c % n * cm, w.Height[c], c / n * cm);

            Shot(view, cam, outDir, log, "overview", new Vector3(span / 2, span * 0.55f, -span * 0.25f), new Vector3(span / 2, 0, span / 2));
            Shot(view, cam, outDir, log, "main-island", mainPos + new Vector3(-main.Radius * cm * 1.1f, 2500, -main.Radius * cm * 1.1f), mainPos);
            var eye = Cell(ra) + Vector3.up * 120f;
            var look = Cell(rb);
            Shot(view, cam, outDir, log, "river-low", eye, new Vector3(look.x, eye.y - 60f, look.z));
            var peak = Enumerable.Range(0, n * n).OrderByDescending(c => w.Height[c]).First();
            var top = Cell(peak);
            Shot(view, cam, outDir, log, "from-peak", top + new Vector3(0, 40f, 0), new Vector3(span / 2, top.y * 0.6f, span / 2));
            var slope = Vector3.Lerp(mainPos, new Vector3(top.x, 0, top.z), 0.6f);
            slope.y = view.GroundHeight(slope) + 30f;
            var toward = new Vector3(top.x, top.y * 0.8f, top.z);
            Shot(view, cam, outDir, log, "ground-close", slope, toward);

            log.Insert(0, view.Stats().Split('\n')[0] + "\n");
            File.WriteAllText(Path.Combine(outDir, "stats.txt"), log.ToString());
            Debug.Log("Stabby capture:\n" + log);
        }

        static void Shot(WorldView view, Camera cam, string dir, StringBuilder log, string name, Vector3 eye, Vector3 target)
        {
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
            view.UpdateLod(cam);
            var rt = new RenderTexture(1600, 900, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(tex);
            log.AppendLine($"{name}: eye {eye.x:F0},{eye.y:F0},{eye.z:F0} | {view.Stats().Split('\n')[1]}");
        }
    }
}
