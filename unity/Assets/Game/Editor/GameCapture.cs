using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;

namespace Stabby.Game.Editor
{
    [InitializeOnLoad]
    public static class GameCapture
    {
        const string Key = "Stabby.GameCapture", StartKey = "Stabby.GameCapture.Start", ScenePath = "Assets/Scenes/Game.unity";
        const double TimeoutSeconds = 240;
        static int settle = -1;

        static GameCapture()
        {
            if (SessionState.GetBool(Key, false)) EditorApplication.update += Poll;
        }

        [MenuItem("Stabby/Build Game Scene")]
        public static void BuildScene()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Viewer/Terrain.mat");
            var sky = new Color(0.62f, 0.74f, 0.86f);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.1f;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.transform.rotation = Quaternion.Euler(38f, 140f, 0);

            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = sky;
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 60000f;
            cam.fieldOfView = 60f;

            new GameObject("Game").AddComponent<GameScene>().TerrainMaterial = mat;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = sky;
            RenderSettings.fogStartDistance = 6000f;
            RenderSettings.fogEndDistance = 55000f;
            RenderSettings.ambientMode = AmbientMode.Flat;

            EditorSceneManager.SaveScene(scene, ScenePath);
            if (!EditorBuildSettings.scenes.Any(s => s.path == ScenePath))
                EditorBuildSettings.scenes = EditorBuildSettings.scenes.Append(new EditorBuildSettingsScene(ScenePath, true)).ToArray();
        }

        public static void Run()
        {
            if (File.Exists(ScenePath)) EditorSceneManager.OpenScene(ScenePath);
            else BuildScene();
            SessionState.SetFloat(StartKey, (float)EditorApplication.timeSinceStartup);
            SessionState.SetBool(Key, true);
            EditorApplication.update += Poll;
            EditorApplication.EnterPlaymode();
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup - SessionState.GetFloat(StartKey, 0) > TimeoutSeconds)
            {
                Debug.LogError("GameCapture: timed out\n" + Status());
                Finish(2);
                return;
            }
            if (!EditorApplication.isPlaying) return;
            SpikeProbe.Tick();
            if (GameStatus.Failure != null)
            {
                Debug.LogError("GameCapture: " + GameStatus.Failure + "\n" + Status());
                Finish(1);
                return;
            }
            bool ready = GameStatus.Synced.Count >= GameSettings.Clients.Length && GameStatus.TerrainBuilt && GameStatus.Capsules >= 2;
            if (!ready) return;
            if (settle < 0) settle = 240;
            if (--settle > 0) return;

            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "out", "unity", "game"));
            Directory.CreateDirectory(dir);
            var cam = Camera.main;
            Shot(cam, dir, "chase");
            var focus = cam.transform.position + cam.transform.forward * 24f;
            cam.transform.SetPositionAndRotation(focus + new Vector3(-30, 28, -38), Quaternion.LookRotation(new Vector3(30, -28, 38)));
            Shot(cam, dir, "overhead");
            GameStatus.Lines["~prediction"] = Prediction();
            GameStatus.Lines["~spikes"] = SpikeProbe.Report();
            File.WriteAllText(Path.Combine(dir, "stats.txt"), Status());
            Debug.Log("GameCapture:\n" + Status());
            Finish(0);
        }

        static string Prediction()
        {
            var lines = new System.Collections.Generic.List<string>();
            Unity.Entities.World server = null;
            var clients = new System.Collections.Generic.List<Unity.Entities.World>();
            for (int i = 0; i < Unity.Entities.World.All.Count; i++)
            {
                var w = Unity.Entities.World.All[i];
                if (w.IsServer()) server = w;
                else if (w.IsClient()) clients.Add(w);
            }
            if (server == null) return "prediction: no server world";
            foreach (var client in clients)
            {
                var q = client.EntityManager.CreateEntityQuery(typeof(Player), typeof(GhostOwner), typeof(LocalTransform), typeof(PredictedGhost));
                foreach (var e in q.ToEntityArray(Unity.Collections.Allocator.Temp))
                {
                    int owner = client.EntityManager.GetComponentData<GhostOwner>(e).NetworkId;
                    var p = client.EntityManager.GetComponentData<LocalTransform>(e).Position;
                    var sq = server.EntityManager.CreateEntityQuery(typeof(Player), typeof(GhostOwner), typeof(LocalTransform));
                    foreach (var s in sq.ToEntityArray(Unity.Collections.Allocator.Temp))
                        if (server.EntityManager.GetComponentData<GhostOwner>(s).NetworkId == owner)
                            lines.Add($"{client.Name}: predicts own player {owner}, {Unity.Mathematics.math.distance(p, server.EntityManager.GetComponentData<LocalTransform>(s).Position):0.00} m ahead of the server");
                }
            }
            return lines.Count > 0 ? string.Join("\n", lines) : "prediction: no predicted ghosts";
        }

        static string Status() =>
            string.Join("\n", GameStatus.Lines.OrderBy(kv => kv.Key).Select(kv => kv.Value)) +
            $"\ncapsules {GameStatus.Capsules}, terrain {(GameStatus.TerrainBuilt ? "built" : "missing")}\n";

        static void Finish(int code)
        {
            EditorApplication.update -= Poll;
            SpikeProbe.Stop();
            SessionState.EraseBool(Key);
            EditorApplication.Exit(code);
        }

        static void Shot(Camera cam, string dir, string name)
        {
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
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }
    }
}
