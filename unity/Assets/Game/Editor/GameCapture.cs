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
        const float UndercutMaxR = 25f;
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

            var water = AssetDatabase.LoadAssetAtPath<Material>("Assets/Viewer/Water.mat");
            if (water == null)
            {
                water = new Material(Shader.Find("Stabby/Water"));
                AssetDatabase.CreateAsset(water, "Assets/Viewer/Water.mat");
            }
            var game = new GameObject("Game").AddComponent<GameScene>();
            game.TerrainMaterial = mat;
            game.WaterMaterial = water;

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
            BuildScene();
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
                home = cam.transform.position;
                GameStatus.Lines["~bench chase"] = Bench(cam, "chase");
                var focus = cam.transform.position + cam.transform.forward * 24f;
                var eye = focus + new Vector3(-30, 28, -38);
                for (int k = 0; k < 8; k++)
                {
                    var at = Vector3.Lerp(eye, focus, k / 8f);
                    eye.y += Mathf.Max(0f, GroundHeight(at.x, at.z) + 10f - at.y) / (1f - k / 8f);
                }
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
                Shot(cam, dir, "overhead");
                var fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                cam.transform.SetPositionAndRotation(home + new Vector3(0, 90, 0), Quaternion.LookRotation(fwd - new Vector3(0, 0.25f, 0)));
                Shot(cam, dir, "far");
                GameStatus.Lines["~bench far"] = Bench(cam, "far");
                GameStatus.HoldCamera = true;
                phase = 1;
            }
            while (phase <= 6)
            {
                string name = phase == 1 ? "arch" : phase == 2 ? "overhang" : phase == 3 ? "cave" : phase == 4 ? "cave-inside" : phase == 5 ? "river" : "undercut";
                if (wait == 0)
                {
                    if (!(phase == 5 ? River(cam) : Aim(cam, phase == 1 ? FeatureEdit.Arch : phase == 3 || phase == 4 ? FeatureEdit.Cave : FeatureEdit.Overhang, phase == 4, phase == 6))) { GameStatus.Lines["~" + name] = "no " + name + " placed"; phase++; continue; }
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
            while (phase <= 10)
            {
                string name = "close-" + CloseNames[phase - 7];
                if (wait == 0)
                {
                    if (!CloseUp(cam, phase - 7)) { GameStatus.Lines["~" + name] = "no " + name + " found"; phase++; continue; }
                    wait = 180;
                    return;
                }
                if (--wait > 0 || (GameStatus.TerrainBehind > 0 && wait > -1800)) return;
                Shot(cam, null, "warmup");
                Shot(cam, dir, name);
                if (phase == 7) GameStatus.Lines["~bench rock"] = Bench(cam, "rock close-up");
                wait = 0;
                phase++;
            }
            if (phase == 11)
            {
                if (wait == 0)
                {
                    if (!Objects(cam)) { GameStatus.Lines["~objects"] = "no objects placed"; phase++; }
                    else { wait = 180; return; }
                }
                else
                {
                    if (--wait > 0 || ((GameStatus.TerrainBehind > 0 || ObjectViewSystem.Pending > 0) && wait > -1800)) return;
                    Shot(cam, null, "warmup");
                    if (wait < 0) GameStatus.Lines["~objects wait"] = $"objects waited {-wait} extra frames";
                    Shot(cam, dir, "objects");
                    GameStatus.Lines["~objects draw"] = DrawStats();
                    wait = 0;
                    phase++;
                }
            }
            if (phase == 12)
            {
                if (tourStop < 0)
                {
                    framesBeforeTour = GameStatus.Lines.TryGetValue("~frames", out var f) ? f : "";
                    tourStop = 0;
                }
                if (tourStop < TourStops)
                {
                    if (wait == 0)
                    {
                        if (!TourMove(cam, tourStop)) { tourStop = TourStops; return; }
                        wait = 30;
                        return;
                    }
                    if (--wait > 0 || (ObjectViewSystem.Pending > 0 && wait > -300)) return;
                    tourParts.Add(ObjectViewSystem.PartsHeld);
                    tourChunks.Add(ObjectViewSystem.ChunksHeld);
                    tourLive.Add(ObjectViewSystem.LiveEntities);
                    wait = 0;
                    if (++tourStop < TourStops) return;
                }
                GameStatus.Lines["~objects tour"] = tourParts.Count == 0 ? "objects tour: no stops" :
                    $"objects tour: {tourParts.Count} stops over {TourStops * TourStepM / 1000f:F0} km, parts held per stop {string.Join("/", tourParts)} (max {tourParts.Max()}), chunks held {string.Join("/", tourChunks)}, entities {string.Join("/", tourLive)}, {ObjectViewSystem.ChunksUnloaded} unloaded";
                if (GameStatus.Lines.TryGetValue("~frames", out var after)) GameStatus.Lines["~frames tour"] = "during tour: " + after;
                GameStatus.Lines["~frames"] = framesBeforeTour;
                phase++;
            }
            GameStatus.Lines["~prediction"] = Prediction();
            GameStatus.Lines["~spikes"] = SpikeProbe.Report();
            File.WriteAllText(Path.Combine(dir, "stats.txt"), Status());
            Debug.Log("GameCapture:\n" + Status());
            Finish(0);
        }

        const int TourStops = 8;
        const float TourStepM = 1500f;
        static int tourStop = -1;
        static string framesBeforeTour = "";
        static readonly System.Collections.Generic.List<int> tourParts = new System.Collections.Generic.List<int>(), tourChunks = new System.Collections.Generic.List<int>(), tourLive = new System.Collections.Generic.List<int>();

        static bool TourMove(Camera cam, int stop)
        {
            foreach (var w in Unity.Entities.World.All)
            {
                if (w.Name != GameSettings.ViewClient) continue;
                var map = w.GetExistingSystemManaged<ClientMapSystem>().Map;
                float size = map.Config.Size * map.Config.CellMeters, margin = 1000f;
                var dir = new Vector3(size * 0.5f - home.x, 0, size * 0.5f - home.z);
                dir = dir.sqrMagnitude > 1f ? dir.normalized : Vector3.right;
                var p = home + dir * (TourStepM * (stop + 1));
                p.x = Mathf.Clamp(p.x, margin, size - margin);
                p.z = Mathf.Clamp(p.z, margin, size - margin);
                var ground = w.EntityManager.CreateEntityQuery(typeof(Ground)).GetSingleton<Ground>();
                p.y = Mathf.Max(ground.HeightAt(p.x, p.z), 0f) + 40f;
                cam.transform.SetPositionAndRotation(p, Quaternion.LookRotation(dir - new Vector3(0, 0.3f, 0)));
                return true;
            }
            return false;
        }

        static string DrawStats()
        {
            var lines = new System.Collections.Generic.List<string> { $"objects draw: {ObjectViewSystem.LiveEntities} object entities ({ObjectViewSystem.PerDef()})" };
            foreach (var w in Unity.Entities.World.All)
            {
                if (w.Name != GameSettings.ViewClient) continue;
                var eg = w.GetExistingSystemManaged<Unity.Rendering.EntitiesGraphicsSystem>();
                if (eg == null) continue;
                var st = eg.Stats;
                lines.Add($"entities graphics (all entities, all views): {st.RenderedInstanceCount} instances rendered, {st.DrawCommandCount} draw commands, {st.DrawRangeCount} draw ranges, {st.BatchCount} batches, {st.ChunkTotal} chunks, {st.BytesGPUMemoryUsed / (1024 * 1024)} MB GPU");
            }
            return string.Join("\n", lines);
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

        static bool Objects(Camera cam)
        {
            foreach (var w in Unity.Entities.World.All)
            {
                var field = w.Name == GameSettings.ViewClient ? w.GetExistingSystemManaged<ObjectColumnsSystem>()?.Field : null;
                if (field == null) continue;
                var map = w.GetExistingSystemManaged<ClientMapSystem>().Map;
                float span = WorldChunks.Size * map.Config.CellMeters, best = float.MaxValue;
                int n = field.ChunksAcross;
                ObjectInstance pick = default;
                bool found = false;
                for (int cz = 0; cz < n; cz++)
                    for (int cx = 0; cx < n; cx++)
                    {
                        if (Mathf.Abs((cx + 0.5f) * span - home.x) > span * 1.5f || Mathf.Abs((cz + 0.5f) * span - home.z) > span * 1.5f) continue;
                        foreach (var o in field.Chunk(cx, cz))
                        {
                            if (!field.Library.Objects[o.Def].Id.StartsWith("boulder", System.StringComparison.Ordinal)) continue;
                            float d = new Vector2(o.X - home.x, o.Z - home.z).magnitude;
                            if (d < best) { best = d; pick = o; found = true; }
                        }
                    }
                if (!found) return false;
                var target = new Vector3(pick.X, pick.Y + pick.Size * 0.4f, pick.Z);
                float back = Mathf.Max(8f, pick.Size * 3.5f);
                var eye = target + new Vector3(back * 0.8f, back * 0.45f, -back * 0.6f);
                var ground = w.EntityManager.CreateEntityQuery(typeof(Ground)).GetSingleton<Ground>();
                eye.y = Mathf.Max(eye.y, ground.HeightAt(eye.x, eye.z) + 3f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                GameStatus.Lines["~aim objects"] = $"{field.Library.Objects[pick.Def].Id} size {pick.Size:F1} m at {pick.X:F0},{pick.Z:F0}, {best:F0} m from the player";
                return true;
            }
            return false;
        }

        static bool River(Camera cam)
        {
            foreach (var w in Unity.Entities.World.All)
            {
                var sys = w.GetExistingSystemManaged<ClientMapSystem>();
                var map = sys?.Map;
                if (map == null) continue;
                float cm = map.Config.CellMeters;
                RiverSegment best = null;
                foreach (var r in map.Water.Rivers) if (r.X.Count > 12 && (best == null || r.Width[r.Width.Count / 2] > best.Width[best.Width.Count / 2])) best = r;
                if (best == null) return false;
                int k = best.X.Count / 2;
                var target = new Vector3(best.X[k] * cm, best.Surface[k], best.Y[k] * cm);
                var along = new Vector3(best.X[k + 4] - best.X[k - 4], 0, best.Y[k + 4] - best.Y[k - 4]).normalized;
                var side = new Vector3(-along.z, 0, along.x);
                var eye = target - along * 160f + side * 70f + Vector3.up * 70f;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                GameStatus.Lines["~aim river"] = $"river width {best.Width[k]:F0} m at {target.x:F0},{target.z:F0}";
                return true;
            }
            return false;
        }

        static float GroundHeight(float x, float z)
        {
            foreach (var w in Unity.Entities.World.All)
            {
                var q = w.EntityManager.CreateEntityQuery(typeof(Ground));
                if (q.CalculateEntityCount() == 1) return q.GetSingleton<Ground>().HeightAt(x, z);
            }
            return float.NegativeInfinity;
        }

        static bool Aim(Camera cam, int shape, bool inside, bool undercut)
        {
            foreach (var w in Unity.Entities.World.All)
            {
                var q = w.EntityManager.CreateEntityQuery(typeof(Ground));
                if (q.CalculateEntityCount() != 1) continue;
                var ground = q.GetSingleton<Ground>();
                ref var g = ref ground.Blob.Value;
                int at = -1;
                for (int k = 0; k < g.Edits.Length && at < 0; k++) if (g.Edits[k].Shape == shape && (shape != FeatureEdit.Overhang || (g.Edits[k].R < UndercutMaxR) == undercut)) at = k;
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
                    eye = undercut ? mid + side * 110f + along * 50f + Vector3.up * 15f : mid + side * 170f + along * 90f + Vector3.up * 35f;
                }
                eye.y = Mathf.Max(eye.y, ground.HeightAt(eye.x, eye.z) + 6f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                GameStatus.Lines["~aim " + shape] = $"edit {at} at {mid.x:F0},{mid.y:F0},{mid.z:F0}";
                return true;
            }
            return false;
        }

        static Vector3 home;
        static readonly string[] CloseNames = { "rock", "grass", "sand", "snow" };

        static bool CloseUp(Camera cam, int which)
        {
            foreach (var w in Unity.Entities.World.All)
            {
                var sys = w.GetExistingSystemManaged<ClientMapSystem>();
                var map = sys?.Map;
                var q = w.EntityManager.CreateEntityQuery(typeof(Ground));
                if (map == null || sys.Materials?.Surface == null || q.CalculateEntityCount() != 1) continue;
                var ground = q.GetSingleton<Ground>();
                int n = map.Config.Size, best = -1;
                float cm = map.Config.CellMeters, bestD = float.MaxValue;
                Vector2 down = default;
                for (int z = 1; z < n - 1; z++)
                    for (int x = 1; x < n - 1; x++)
                    {
                        int i = z * n + x;
                        if (map.Sea[i] || map.Height[i] < (which == 2 ? 0.5f : 3)) continue;
                        float gx = (map.Height[i + 1] - map.Height[i - 1]) / (2 * cm), gz = (map.Height[i + n] - map.Height[i - n]) / (2 * cm), s = Mathf.Sqrt(gx * gx + gz * gz);
                        bool ok = which == 0 ? s > 0.8f : which == 2 && map.Height[i] < 3 ? s < 0.1f : s < 0.12f && map.Height[i] >= 3 && map.Biome != null && map.Biome[i] >= 0 && sys.Materials.Surface.CoatOf(map.BiomeIds[map.Biome[i]]) == (which == 1 ? 0 : which);
                        float d = (new Vector2(x * cm, z * cm) - new Vector2(home.x, home.z)).sqrMagnitude;
                        if (ok && d < bestD) { bestD = d; best = i; down = new Vector2(-gx, -gz).normalized; }
                    }
                if (best < 0) return false;
                var target = new Vector3(best % n * cm, 0, best / n * cm);
                target.y = ground.HeightAt(target.x, target.z);
                var eye = which == 0 ? target + new Vector3(down.x, 0, down.y) * 14f + Vector3.up * 2f : target + new Vector3(-0.77f, 0, -0.64f) * 5f + Vector3.up * 2.5f;
                eye.y = Mathf.Max(eye.y, ground.HeightAt(eye.x, eye.z) + 1.5f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                GameStatus.Lines["~aim close " + which] = $"{CloseNames[which]} at {target.x:F0},{target.y:F0},{target.z:F0}, {Mathf.Sqrt(bestD):F0} m from the player";
                return true;
            }
            return false;
        }

        static string Bench(Camera cam, string label)
        {
            var sb = new System.Text.StringBuilder($"bench {label} (ms, base/full):");
            foreach (var (w, h) in new[] { (1920, 1080), (2560, 1440), (3840, 2160) })
            {
                Shader.SetGlobalFloat("_StabbyDetailOff", 1);
                double b = BenchAt(cam, w, h);
                Shader.SetGlobalFloat("_StabbyDetailOff", 0);
                double f = BenchAt(cam, w, h);
                sb.Append($" {h}p {b:F2}/{f:F2}");
            }
            return sb.ToString();
        }

        static double BenchAt(Camera cam, int w, int h)
        {
            GameStatus.ToolFrame = Time.frameCount;
            var rt = new RenderTexture(w, h, 24);
            var px = new Texture2D(1, 1, TextureFormat.RGB24, false);
            cam.targetTexture = rt;
            RenderTexture.active = rt;
            for (int k = 0; k < 5; k++) cam.Render();
            px.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
            const int frames = 60;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (int k = 0; k < frames; k++) { cam.Render(); px.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); }
            double ms = clock.Elapsed.TotalMilliseconds / frames;
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(px);
            return ms;
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
