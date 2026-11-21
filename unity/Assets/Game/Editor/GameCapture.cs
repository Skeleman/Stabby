using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using Stabby.World;

namespace Stabby.Game.Editor
{
    [InitializeOnLoad]
    public static class GameCapture
    {
        const string Key = "Stabby.GameCapture", StartKey = "Stabby.GameCapture.Start", ScenePath = "Assets/Scenes/Game.unity";
        const double TimeoutSeconds = 240;
        static int settle = -1, phase, wait;
        static double playAt = -1;

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
            if (playAt < 0) playAt = EditorApplication.timeSinceStartup;
            if (!quiet) quiet = QuietBatchWarning();
            SpikeProbe.Tick();
            if (GameStatus.Failure != null)
            {
                Debug.LogError("GameCapture: " + GameStatus.Failure + "\n" + Status());
                Finish(1);
                return;
            }
            bool ready = GameStatus.Synced.Count >= GameSettings.Clients.Length && GameStatus.TerrainBuilt && GameStatus.Capsules >= 2;
            if (!ready) return;
            if (settle == -1) { settle = 240; GameStatus.Lines["~startup"] = $"playable {EditorApplication.timeSinceStartup - playAt:F2} s after entering play mode"; }
            if (settle > 0 && --settle > 0) return;

            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "out", "unity", "game"));
            Directory.CreateDirectory(dir);
            var cam = Camera.main;
            if (phase == 0)
            {
                Shot(cam, null, "warmup");
                Shot(cam, dir, "chase");
                var focus = cam.transform.position + cam.transform.forward * 24f;
                cam.transform.SetPositionAndRotation(focus + new Vector3(-30, 28, -38), Quaternion.LookRotation(new Vector3(30, -28, 38)));
                Shot(cam, dir, "overhead");
                GameStatus.HoldCamera = true;
                phase = 1;
            }
            while (phase <= 4)
            {
                string name = phase == 1 ? "arch" : phase == 2 ? "overhang" : phase == 3 ? "cave" : "cave-inside";
                if (wait == 0)
                {
                    if (!Aim(cam, phase == 1 ? FeatureEdit.Arch : phase == 2 ? FeatureEdit.Overhang : FeatureEdit.Cave, phase == 4)) { GameStatus.Lines["~" + name] = "no " + name + " placed"; phase++; continue; }
                    wait = 180;
                    return;
                }
                if (--wait > 0 || (GameStatus.TerrainBehind > 0 && wait > -1800)) return;
                Shot(cam, null, "warmup");
                if (wait < 0) GameStatus.Lines["~" + name + " wait"] = $"{name} waited {-wait} extra frames for terrain";
                Shot(cam, dir, name);
                if (phase == 2) Wire(cam, dir, name + "-wire");
                wait = 0;
                phase++;
            }
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

        static bool Aim(Camera cam, int shape, bool inside)
        {
            foreach (var w in Unity.Entities.World.All)
            {
                var q = w.EntityManager.CreateEntityQuery(typeof(Ground));
                if (q.CalculateEntityCount() != 1) continue;
                var ground = q.GetSingleton<Ground>();
                ref var g = ref ground.Blob.Value;
                int at = -1;
                for (int k = 0; k < g.Edits.Length && at < 0; k++) if (g.Edits[k].Shape == shape) at = k;
                if (at < 0) return false;
                var first = g.Edits[at];
                var last = shape == FeatureEdit.Arch ? g.Edits[at + 4] : first;
                var a = new Vector3(first.Ax, first.Ay, first.Az);
                var b = new Vector3(last.Bx, last.By, last.Bz);
                var mid = (a + b) * 0.5f;
                var along = new Vector3(b.x - a.x, 0, b.z - a.z);
                float len = along.magnitude;
                along /= len;
                var side = new Vector3(-along.z, 0, along.x);
                Vector3 eye, target;
                if (shape == FeatureEdit.Arch)
                {
                    var top = g.Edits[at + 2];
                    target = new Vector3(mid.x, (mid.y + top.By) * 0.5f, mid.z);
                    eye = target + side * len * 1.2f + Vector3.up * len * 0.15f;
                }
                else if (inside)
                {
                    var next = g.Edits[at + 1];
                    float lift = 4f - first.R * Features.CaveHollow;
                    eye = new Vector3(first.Ax, first.Ay + lift, first.Az) + (b - a) * 0.6f;
                    target = new Vector3(next.Bx, next.By + lift, next.Bz);
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                    GameStatus.Lines["~aim inside"] = $"edit {at} from {eye.x:F0},{eye.y:F0},{eye.z:F0}";
                    return true;
                }
                else if (shape == FeatureEdit.Cave)
                {
                    target = a + along * 30f;
                    eye = a - along * 110f + side * 40f + Vector3.up * 20f;
                }
                else
                {
                    if (ground.HeightAt(mid.x + side.x * 60, mid.z + side.z * 60) > ground.HeightAt(mid.x - side.x * 60, mid.z - side.z * 60)) side = -side;
                    target = mid;
                    eye = mid + side * 170f + along * 90f + Vector3.up * 35f;
                }
                eye.y = Mathf.Max(eye.y, ground.HeightAt(eye.x, eye.z) + 6f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                GameStatus.Lines["~aim " + shape] = $"edit {at} at {mid.x:F0},{mid.y:F0},{mid.z:F0}";
                return true;
            }
            return false;
        }

        static void Wire(Camera cam, string dir, string name)
        {
            GameStatus.ToolFrame = Time.frameCount;
            System.Action<ScriptableRenderContext, Camera> on = (c, k) => GL.wireframe = true, off = (c, k) => GL.wireframe = false;
            RenderPipelineManager.beginCameraRendering += on;
            RenderPipelineManager.endCameraRendering += off;
            Shot(cam, dir, name);
            RenderPipelineManager.beginCameraRendering -= on;
            RenderPipelineManager.endCameraRendering -= off;
            GL.wireframe = false;
        }

        static bool quiet;

        static bool QuietBatchWarning()
        {
            foreach (var w in Unity.Entities.World.All)
            {
                if (!w.IsServer()) continue;
                using var q = w.EntityManager.CreateEntityQuery(typeof(Unity.NetCode.NetDebug));
                if (!q.TryGetSingletonRW<Unity.NetCode.NetDebug>(out var nd)) return false;
                nd.ValueRW.WarnBatchedTicks = false;
                return true;
            }
            return false;
        }

        static void Shot(Camera cam, string dir, string name)
        {
            GameStatus.ToolFrame = Time.frameCount;
            var rt = new RenderTexture(1600, 900, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            if (dir != null) File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }
    }
}
