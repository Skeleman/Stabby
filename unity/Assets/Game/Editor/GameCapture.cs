using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
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
        const double TimeoutSeconds = 480;
        static int settle = -1, phase, wait, smooth, chaseWait, impStep;
        static readonly string only = System.Environment.GetEnvironmentVariable("STABBY_CAPTURE_ONLY");
        static bool bank, lake;
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
            sun.intensity = 0.8f;
            sun.shadows = LightShadows.Soft;
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
            int port = FreeUdpPort();
            System.Environment.SetEnvironmentVariable(GameSettings.PortVariable, port.ToString());
            Debug.Log($"GameCapture: using UDP port {port}");
            SessionState.SetFloat(StartKey, (float)EditorApplication.timeSinceStartup);
            SessionState.SetBool(Key, true);
            EditorApplication.update += Poll;
            EditorApplication.EnterPlaymode();
        }

        static int FreeUdpPort()
        {
            using var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp);
            socket.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
            return ((System.Net.IPEndPoint)socket.LocalEndPoint).Port;
        }

        static void Poll()
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            PollBody();
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (ms >= 10) GameStatus.ToolFrame = Time.frameCount;
            SpikeProbe.Exit(ms, phase, wait, tourStop);
        }

        static string dir;

        static void PollBody()
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
            if (phase == 0 && GameStatus.TerrainBehind > 0 && chaseWait < 1800) { chaseWait++; return; }

            if (dir == null) { dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "out", "unity", "game")); Directory.CreateDirectory(dir); }
            var cam = Camera.main;
            if (phase == 0)
            {
                Shot(cam, null, "warmup");
                if (chaseWait > 0) GameStatus.Lines["~chase wait"] = $"chase waited {chaseWait} extra frames for terrain";
                Shot(cam, dir, "chase");
                home = cam.transform.position;
                GameStatus.Lines["~bench chase"] = Bench(cam, "chase");
                GameStatus.Lines["~bench chase blades"] = GrassViewSystem.Counts();
                GameStatus.Lines["~bench chase shadows"] = ShadowBench(cam, "chase", true);
                GrassViewSystem.Rescatter = true;
                GameStatus.Lines["~bench chase moving"] = Bench(cam, "chase rescattering grass every frame");
                GrassViewSystem.Rescatter = false;
                var fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                var focus = home + fwd * 22f;
                focus.y = GroundHeight(focus.x, focus.z);
                var eye = focus - fwd * 300f + Vector3.up * 800f;
                eye.y = Mathf.Max(eye.y, GroundHeight(eye.x, eye.z) + 500f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
                Shot(cam, dir, "overhead");
                cam.transform.SetPositionAndRotation(home + new Vector3(0, 90, 0), Quaternion.LookRotation(fwd - new Vector3(0, 0.25f, 0)));
                Shot(cam, dir, "far");
                GameStatus.Lines["~bench far"] = Bench(cam, "far");
                GameStatus.Lines["~bench far blades"] = GrassViewSystem.Counts();
                GameStatus.Lines["~bench far shadows"] = ShadowBench(cam, "far", true);
                GameStatus.HoldCamera = true;
                phase = only == "cliffs" ? 50 : only == "rock" ? 7 : only == "coast" ? 9 : only == "grass" ? 8 : only == "biomes" ? 9 : only == "beach" ? 40 : only == "forest" ? 60 : 1;
            }
            if (phase == 50)
            {
                if (job == null) job = CliffTour(cam, dir);
                if (job.MoveNext()) return;
                File.WriteAllText(Path.Combine(dir, "stats.txt"), Status());
                Finish(0);
                return;
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
                if (--wait > 0 || ((GameStatus.TerrainBehind > 0 || ObjectViewSystem.Pending > 0) && wait > -1800)) { if (wait == 0) wait = -1; return; }
                Shot(cam, null, "warmup");
                if (wait < 0) GameStatus.Lines["~" + name + " wait"] = $"{name} waited {-wait} extra frames for terrain";
                Shot(cam, dir, phase == 5 && lake ? "lake" : phase == 5 && bank ? "riverbank" : name);
                if (phase == 2) Wire(cam, dir, name + "-wire");
                if (phase == 5 && bank && !lake && GameStatus.Lines.TryGetValue("~grass view", out var gv)) GameStatus.Lines["~riverbank grass"] = "riverbank " + gv;
                if (phase == 5 && !bank && Bank(cam)) { bank = true; wait = 180; return; }
                if (phase == 5 && !lake && AimLake(cam)) { lake = true; wait = 180; return; }
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
                if (phase == 7 && smooth < 2)
                {
                    if (smooth == 1)
                    {
                        Shot(cam, null, "warmup");
                        Shot(cam, dir, name + "-smooth");
                    }
                    SetSmooth(smooth == 0);
                    smooth++;
                    wait = 180;
                    return;
                }
                Shot(cam, null, "warmup");
                Shot(cam, dir, name);
                if (phase == 7) GameStatus.Lines["~bench rock"] = Bench(cam, "rock close-up");
                if (phase == 7) GameStatus.Lines["~bench rock shadows"] = ShadowBench(cam, "rock close-up", false);
                if (phase == 9) GameStatus.Lines["~bench sand"] = Bench(cam, "sand close-up");
                if (phase == 7 && only == "rock")
                {
                    File.WriteAllText(Path.Combine(dir, "stats.txt"), Status());
                    Finish(0);
                    return;
                }
                if (phase == 7)
                {
                    lows.Add(("rock", cam.transform.position, cam.transform.rotation));
                    lows.Add(("cliff", cam.transform.position, cam.transform.rotation));
                    Vector3 fwd = cam.transform.forward, at = cam.transform.position + fwd * 12f, back = cam.transform.position - new Vector3(fwd.x, 0, fwd.z).normalized * 30f + Vector3.up * 10f;
                    lows.Add(("cliff-wide", back, Quaternion.LookRotation(at - back)));
                }
                if (phase == 8)
                {
                    GameStatus.Lines["~bench grass"] = Bench(cam, "grass close-up");
                    GameStatus.Lines["~bench grass blades"] = GrassViewSystem.Counts();
                    lows.Add(("grass", cam.transform.position, cam.transform.rotation));
                    GrassViewSystem.Rescatter = true;
                    GameStatus.Lines["~bench grass moving"] = Bench(cam, "grass close-up rescattering grass every frame");
                    GrassViewSystem.Rescatter = false;
                    GrassViewSystem.Paused = true;
                    GameStatus.Lines["~bench grass off"] = Bench(cam, "grass close-up without blades");
                    GrassViewSystem.Paused = false;
                    var gf = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                    var gp = cam.transform.position;
                    gp.y = GroundHeight(gp.x, gp.z) + 1.7f;
                    cam.transform.SetPositionAndRotation(gp, Quaternion.LookRotation(gf - new Vector3(0, 0.12f, 0)));
                    Shot(cam, null, "warmup");
                    Shot(cam, dir, "grass-fade");
                    var mp = gp - gf * 18f;
                    mp.y = GroundHeight(mp.x, mp.z) + 4.5f;
                    cam.transform.SetPositionAndRotation(mp, Quaternion.LookRotation(gf - new Vector3(0, 0.2f, 0)));
                    Shot(cam, null, "warmup");
                    Shot(cam, dir, "grass-meadow");
                    if (only == "grass")
                    {
                        File.WriteAllText(Path.Combine(dir, "stats.txt"), Status());
                        Finish(0);
                        return;
                    }
                }
                if (phase == 9)
                {
                    var sf = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                    var sp = cam.transform.position - sf * 15f;
                    sp.y = GroundHeight(sp.x, sp.z) + 2.5f;
                    cam.transform.SetPositionAndRotation(sp, Quaternion.LookRotation(sf - new Vector3(0, 0.12f, 0)));
                    Shot(cam, null, "warmup");
                    Shot(cam, dir, "sand-far");
                }
                if (phase == 10)
                {
                    var wf = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                    var at = cam.transform.position + wf * 15f;
                    at.y = GroundHeight(at.x, at.z);
                    var wpos = cam.transform.position - wf * 35f;
                    wpos.y = Mathf.Max(GroundHeight(wpos.x, wpos.z), at.y) + 16f;
                    cam.transform.SetPositionAndRotation(wpos, Quaternion.LookRotation(at - wpos));
                    Shot(cam, null, "warmup");
                    Shot(cam, dir, "snow-wide");
                }
                wait = 0;
                phase = phase == 9 && only == "coast" ? 13 : phase == 10 && only == "biomes" ? 26 : phase + 1;
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
                    var sunFlat = Vector3.ProjectOnPlane(Object.FindFirstObjectByType<Light>().transform.forward, Vector3.up).normalized;
                    var objRot = cam.transform.rotation;
                    cam.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(-sunFlat, Vector3.up) + Vector3.up * cam.transform.forward.y);
                    Shot(cam, null, "warmup");
                    Shot(cam, dir, "objects-shadows");
                    cam.transform.rotation = objRot;
                    GameStatus.Lines["~objects draw"] = DrawStats();
                    wait = 0;
                    phase++;
                }
            }
            if (phase == 12)
            {
                if (tourStop < 0) tourStop = 0;
                SpikeProbe.Touring = true;
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
                SpikeProbe.Touring = false;
                GameStatus.PutFrames();
                if (GameStatus.Lines.TryGetValue("~frames", out var after)) GameStatus.Lines["~frames tour"] = "during tour: " + after;
                phase++;
            }
            while (phase >= 13 && phase <= 15)
            {
                string name = phase == 13 ? "coast-low" : phase == 14 ? "coast-high" : "coast-far";
                if (wait == 0)
                {
                    if (!(phase == 15 ? FarCoast(cam, 1000f, 250f) : Coast(cam, phase == 13 ? 40f : 150f))) { GameStatus.Lines["~" + name] = "no coast found"; phase++; continue; }
                    wait = 180;
                    return;
                }
                if (--wait > 0 || (GameStatus.TerrainBehind > 0 && wait > -1800)) return;
                Shot(cam, null, "warmup");
                Shot(cam, dir, name);
                if (phase == 14) GameStatus.Lines["~bench water"] = Bench(cam, "water coast");
                if (phase == 14) GameStatus.Lines["~bench water shadows"] = ShadowBench(cam, "water coast", false);
                if (phase == 15) GameStatus.Lines["~bench water far"] = Bench(cam, "water far coast");
                wait = 0;
                phase++;
            }
            if (phase == 16 && only == "coast")
            {
                File.WriteAllText(Path.Combine(dir, "stats.txt"), Status());
                Finish(0);
                return;
            }
            while (phase >= 16 && phase <= 17)
            {
                string name = phase == 16 ? "boulder-close" : "boulder-mid";
                if (wait == 0)
                {
                    if (!AimBoulder(cam, phase == 16)) { GameStatus.Lines["~" + name] = "no boulder placed"; phase++; continue; }
                    wait = 180;
                    return;
                }
                if (--wait > 0 || (GameStatus.TerrainBehind > 0 && wait > -1800)) return;
                Shot(cam, null, "warmup");
                Shot(cam, dir, name);
                if (phase == 16) lows.Add((name, cam.transform.position, cam.transform.rotation));
                wait = 0;
                phase++;
            }
            while (phase >= 18 && phase <= 23)
            {
                string name = FoliageShots[phase - 18];
                if (wait == 0)
                {
                    if (!AimFoliage(cam, FoliageRanges[phase - 18])) { GameStatus.Lines["~" + name] = "no foliage placed"; phase++; continue; }
                    wait = 180;
                    return;
                }
                if (--wait > 0 || ((GameStatus.TerrainBehind > 0 || ObjectViewSystem.Pending > 0) && wait > -1800)) return;
                Shot(cam, null, "warmup");
                Shot(cam, dir, phase == 21 && impStep > 0 ? (impStep == 1 ? "foliage-far-mesh" : "foliage-far-impostor") : name);
                if (phase == 21 && impStep < 2)
                {
                    impStep++;
                    DetailView.ImpostorScale = impStep == 1 ? 0.001f : 1000f;
                    wait = 30;
                    return;
                }
                if (phase == 21) DetailView.ImpostorScale = 1f;
                if (phase == 19)
                    GameStatus.Lines["~bench trees"] = FoliageBench(cam, "trees filling the view");
                if (phase == 20)
                {
                    AimFoliage(cam, 3);
                    Shot(cam, dir, "foliage-bench");
                    GameStatus.Lines["~bench foliage"] = FoliageBench(cam, "foliage looking down on the densest bushes");
                    AimFoliage(cam, 1);
                }
                wait = 0;
                phase++;
            }
            while (phase >= 24 && phase <= 25)
            {
                string name = phase == 24 ? "structure-close" : "structure-mid";
                if (wait == 0)
                {
                    if (!AimStructure(cam, phase == 24)) { GameStatus.Lines["~" + name] = "no structure placed"; phase++; continue; }
                    wait = 180;
                    return;
                }
                if (--wait > 0 || ((GameStatus.TerrainBehind > 0 || ObjectViewSystem.Pending > 0) && wait > -1800)) return;
                Shot(cam, null, "warmup");
                Shot(cam, dir, name);
                if (phase == 25) GameStatus.Lines["~bench structure"] = Bench(cam, "structure mid");
                wait = 0;
                phase++;
            }
            while (phase >= 26 && phase < 26 + BiomeShots.Length)
            {
                string name = "biome-" + BiomeShots[phase - 26];
                if (wait == 0)
                {
                    if (!AimBiome(cam, BiomeShots[phase - 26])) { GameStatus.Lines["~" + name] = "no " + name + " found"; phase++; continue; }
                    wait = 180;
                    return;
                }
                if (--wait > 0 || ((GameStatus.TerrainBehind > 0 || ObjectViewSystem.Pending > 0) && wait > -1800)) return;
                Shot(cam, null, "warmup");
                Shot(cam, dir, name);
                var cu = Shader.GetGlobalVector("_StabbyContactUV");
                if (cu.x > 0 && name.StartsWith("contact")) GameStatus.Lines["~" + name + " contact"] = $"{name}: contact window centre {(0.5f - cu.y) / cu.x:F0},{(0.5f - cu.z) / cu.x:F0}, camera {cam.transform.position.x:F0},{cam.transform.position.z:F0}";
                wait = 0;
                phase++;
            }
            while (phase >= 40 && phase < 40 + 2 * BeachShots.Length)
            {
                int bk = (phase - 40) / 2;
                bool mid = (phase - 40) % 2 == 1;
                string name = "beach-" + BeachShots[bk] + (mid ? "-mid" : "-close");
                if (wait == 0)
                {
                    if (!AimBeach(cam, bk, mid)) { GameStatus.Lines["~" + name] = "no " + name + " found"; phase++; continue; }
                    wait = 180;
                    return;
                }
                if (--wait > 0 || ((GameStatus.TerrainBehind > 0 || ObjectViewSystem.Pending > 0) && wait > -1800)) return;
                Shot(cam, null, "warmup");
                Shot(cam, dir, name);
                wait = 0;
                phase++;
            }
            while (phase >= 60 && phase <= 60 + 2 * ForestShots.Length)
            {
                bool wide = phase == 60 + 2 * ForestShots.Length;
                int fk = wide ? 0 : (phase - 60) / 2, view = wide ? 2 : (phase - 60) % 2;
                string name = "forest-" + ForestShots[fk] + (view == 2 ? "-wide" : view == 1 ? "-mid" : "-close");
                if (wait == 0)
                {
                    if (!AimBiome(cam, ForestShots[fk]) || !AimForest(cam, ForestShots[fk], view)) { GameStatus.Lines["~" + name] = "no " + name + " found"; phase++; continue; }
                    wait = 180;
                    return;
                }
                if (--wait > 0 || ((GameStatus.TerrainBehind > 0 || ObjectViewSystem.Pending > 0) && wait > -1800)) return;
                Shot(cam, null, "warmup");
                Shot(cam, dir, name);
                wait = 0;
                phase++;
            }
            if (only == "biomes" || only == "beach" || only == "forest")
            {
                File.WriteAllText(Path.Combine(dir, "stats.txt"), Status());
                Finish(0);
                return;
            }
            int f0 = 26 + BiomeShots.Length;
            while (phase >= f0 && phase <= f0 + 6)
            {
                string name = phase - f0 < ContactShots.Length ? ContactShots[phase - f0] : "";
                if (wait == 0)
                {
                    if (!(phase < f0 + 3 ? AimFloor(cam, phase - f0) : AimContact(cam, phase - f0 - 3))) { GameStatus.Lines["~" + name] = "no " + name + " found"; phase++; continue; }
                    wait = 180;
                    return;
                }
                if (--wait > 0 || ((GameStatus.TerrainBehind > 0 || ObjectViewSystem.Pending > 0) && wait > -1800)) return;
                Shot(cam, null, "warmup");
                Shot(cam, dir, name);
                var cu = Shader.GetGlobalVector("_StabbyContactUV");
                if (cu.x > 0 && name.StartsWith("contact")) GameStatus.Lines["~" + name + " contact"] = $"{name}: contact window centre {(0.5f - cu.y) / cu.x:F0},{(0.5f - cu.z) / cu.x:F0}, camera {cam.transform.position.x:F0},{cam.transform.position.z:F0}";
                wait = 0;
                phase++;
            }
            if (!castDone) { job = CastTour(cam, dir); castDone = true; }
            if (job != null && job.MoveNext()) return;
            job = null;
            GameStatus.Lines["~prediction"] = Prediction();
            GameStatus.Lines["~spikes"] = SpikeProbe.Report();
            File.WriteAllText(Path.Combine(dir, "stats.txt"), Status());
            Debug.Log("GameCapture:\n" + Status());
            Finish(0);
        }

        const int TourStops = 8;
        const float TourStepM = 1500f;
        static int tourStop = -1;
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

        static string Status()
        {
            GameStatus.PutFrames();
            return string.Join("\n", GameStatus.Lines.OrderBy(kv => kv.Key).Select(kv => kv.Value)) +
            $"\ncapsules {GameStatus.Capsules}, terrain {(GameStatus.TerrainBuilt ? "built" : "missing")}\n";
        }

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

        static bool AimLake(Camera cam)
        {
            foreach (var w in Unity.Entities.World.All)
            {
                var map = w.GetExistingSystemManaged<ClientMapSystem>()?.Map;
                if (map == null) continue;
                Lake pick = null;
                foreach (var l in map.Water.Lakes) if (pick == null || l.Cells.Count > pick.Cells.Count) pick = l;
                if (pick == null) return false;
                int n = map.Config.Size, edge = -1;
                float cm = map.Config.CellMeters, bestD = float.MaxValue;
                var mid = Vector2.zero;
                foreach (int i in pick.Cells) mid += new Vector2(i % n, i / n);
                mid = mid / pick.Cells.Count * cm;
                foreach (int i in pick.Cells)
                {
                    int x = i % n, z = i / n;
                    if (x < 1 || z < 1 || x >= n - 1 || z >= n - 1) continue;
                    if (map.Water.LakeId[i + 1] == pick.Id && map.Water.LakeId[i - 1] == pick.Id && map.Water.LakeId[i + n] == pick.Id && map.Water.LakeId[i - n] == pick.Id) continue;
                    float d = (new Vector2(x * cm, z * cm) - new Vector2(home.x, home.z)).sqrMagnitude;
                    if (d < bestD) { bestD = d; edge = i; }
                }
                if (edge < 0) return false;
                var shore = new Vector2(edge % n * cm, edge / n * cm);
                var dir = (mid - shore).sqrMagnitude > 1f ? (mid - shore).normalized : Vector2.right;
                var eye = new Vector3(shore.x - dir.x * 25f, 0, shore.y - dir.y * 25f);
                eye.y = Mathf.Max(GroundHeight(eye.x, eye.z), pick.Level) + 6f;
                var target = new Vector3(shore.x + dir.x * 60f, pick.Level, shore.y + dir.y * 60f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                GameStatus.Lines["~aim lake"] = $"lake {pick.Id} ({pick.Cells.Count} cells) shore at {shore.x:F0},{shore.y:F0}, {Mathf.Sqrt(bestD):F0} m from the player";
                return true;
            }
            return false;
        }

        static bool Bank(Camera cam)
        {
            foreach (var w in Unity.Entities.World.All)
            {
                var sys = w.GetExistingSystemManaged<ClientMapSystem>();
                var map = sys?.Map;
                if (map == null || map.Biome == null || sys.Materials?.Surface == null) continue;
                int n = map.Config.Size;
                float cm = map.Config.CellMeters, bestD = float.MaxValue;
                Vector3 target = default, eye = default;
                foreach (var r in map.Water.Rivers)
                    for (int k = 4; k + 4 < r.X.Count; k += 4)
                    {
                        var along = new Vector3(r.X[k + 4] - r.X[k - 4], 0, r.Y[k + 4] - r.Y[k - 4]).normalized;
                        var side = new Vector3(-along.z, 0, along.x);
                        var edge = new Vector3(r.X[k] * cm, r.Surface[k], r.Y[k] * cm) + side * (r.Width[k] * 0.5f);
                        var dry = edge + side * 20f;
                        int i = NearField.CellOf(dry.x, dry.z, cm, n);
                        if (map.Sea[i] || map.Water.LakeId[i] >= 0 || map.Biome[i] < 0 || sys.Materials.Surface.CoatOf(map.BiomeIds[map.Biome[i]]) != 0) continue;
                        float d = (new Vector2(edge.x, edge.z) - new Vector2(home.x, home.z)).sqrMagnitude;
                        if (d >= bestD) continue;
                        bestD = d;
                        target = edge - side * 2f;
                        eye = edge + side * 7f + along * 2f;
                    }
                if (bestD == float.MaxValue) return false;
                eye.y = Mathf.Max(GroundHeight(eye.x, eye.z), target.y) + 2.5f;
                target.y = Mathf.Max(GroundHeight(target.x, target.z), target.y);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                GameStatus.Lines["~aim riverbank"] = $"grass riverbank at {target.x:F0},{target.y:F0},{target.z:F0} from {eye.x:F0},{eye.y:F0},{eye.z:F0}, {Mathf.Sqrt(bestD):F0} m from the player";
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

        static readonly float[] FoliageBack = { 2.4f, 3.2f, 60f, 5f, 1.7f, 0.9f, 0.65f }, FoliageUp = { 0.5f, 0.35f, 14f, 7f, 0.1f, 0.15f, 0.04f };
        static readonly int[] FoliageRanges = { 0, 4, 1, 2, 5, 6 };
        static readonly string[] ContactShots = { "floor-near", "floor-mid", "cliff-foot", "contact-gate", "contact-log", "contact-tree", "contact-boulder" };
        static readonly string[] FoliageShots ={ "foliage-close", "tree-close", "foliage-mid", "foliage-far", "spruce-close", "oak-close" };

        static bool AimFoliage(Camera cam, int range)
        {
            foreach (var w in Unity.Entities.World.All)
            {
                var field = w.Name == GameSettings.ViewClient ? w.GetExistingSystemManaged<ObjectColumnsSystem>()?.Field : null;
                if (field == null) continue;
                var map = w.GetExistingSystemManaged<ClientMapSystem>().Map;
                float span = WorldChunks.Size * map.Config.CellMeters, best = float.MaxValue;
                int n = field.ChunksAcross;
                var near = new System.Collections.Generic.List<ObjectInstance>();
                var trees = new System.Collections.Generic.List<ObjectInstance>();
                for (int cz = 0; cz < n; cz++)
                    for (int cx = 0; cx < n; cx++)
                    {
                        if (Mathf.Abs((cx + 0.5f) * span - home.x) > span * 3.5f || Mathf.Abs((cz + 0.5f) * span - home.z) > span * 3.5f) continue;
                        foreach (var o in field.Chunk(cx, cz))
                        {
                            if (field.Library.Objects[o.Def].Parts.Exists(p => p.Shape == ObjectPart.Broadleaf || p.Shape == ObjectPart.Conifer)) trees.Add(o);
                            if (Mathf.Abs((cx + 0.5f) * span - home.x) > span * 3f || Mathf.Abs((cz + 0.5f) * span - home.z) > span * 3f) continue;
                            if (field.Library.Objects[o.Def].Parts.Exists(p => range == 5 ? p.Shape == ObjectPart.Conifer : range == 6 ? p.Shape == ObjectPart.Broadleaf : range == 4 ? p.Shape >= ObjectPart.Broadleaf : range == 0 || range == 3 ? p.Shape == ObjectPart.Shrub : ObjectPart.IsFoliage(p.Shape))) near.Add(o);
                        }
                    }
                if (near.Count == 0) return false;
                bool scaled = range == 0 || range >= 4;
                ObjectInstance pick = default;
                int crowd = 0;
                var cells = new System.Collections.Generic.Dictionary<long, int>();
                long Cell(float x, float z) => ((long)Mathf.FloorToInt(x / 12f) << 32) ^ (uint)Mathf.FloorToInt(z / 12f);
                foreach (var o in near) { long c = Cell(o.X, o.Z); cells.TryGetValue(c, out int k); cells[c] = k + 1; }
                bool clear = range == 1 || range >= 4;
                if (clear) scaled = true;
                var ranked = new System.Collections.Generic.List<(float score, ObjectInstance o, int around)>();
                foreach (var o in near)
                {
                    int around = cells[Cell(o.X, o.Z)];
                    if (range == 1 && field.Library.Objects[o.Def].Parts.TrueForAll(p => p.Shape < ObjectPart.Broadleaf)) continue;
                    float score = new Vector2(o.X - home.x, o.Z - home.z).magnitude - 40f * Mathf.Min(around, 12) - 30f * o.Size;
                    ranked.Add((score, o, around));
                    if (score < best) { best = score; pick = o; crowd = around; }
                }
                if (ranked.Count == 0) return false;
                var dirXZ = new Vector3(-0.77f, 0, -0.64f);
                float BackOf(ObjectInstance o) => FoliageBack[range] * (scaled ? Mathf.Max(1f, o.Size) : 1f);
                bool found = false;
                if (clear)
                {
                    ranked.Sort((a, b) => a.score.CompareTo(b.score));
                    for (int r = 0; r < Mathf.Min(ranked.Count, range == 6 ? 600 : 150) && !found; r++)
                        for (int a = 0; a < 16 && !found; a++)
                        {
                            var c = ranked[r].o;
                            var d = Quaternion.Euler(0, a * 22.5f, 0) * new Vector3(-0.77f, 0, -0.64f);
                            float len = BackOf(c), strict = range == 5 ? 1.6f : range == 6 ? 1.3f : 1f;
                            bool open = true;
                            foreach (var o in range >= 5 ? trees : near)
                            {
                                if (o.Id == c.Id) continue;
                                var rel = new Vector2(o.X - c.X, o.Z - c.Z);
                                if (range == 5 && rel.magnitude < 0.5f * (o.Size + c.Size)) { open = false; break; }
                                float along = Vector2.Dot(rel, new Vector2(d.x, d.z));
                                if (along < -1f || along > len * strict + o.Size * 0.4f) continue;
                                float off = (rel - new Vector2(d.x, d.z) * along).magnitude;
                                if (off < (0.4f * o.Size + 0.25f * c.Size) * strict) { open = false; break; }
                            }
                            if (open) { pick = c; crowd = ranked[r].around; dirXZ = d; found = true; }
                        }
                }
                var target = new Vector3(pick.X, pick.Y + pick.Size * (range == 6 ? 0.32f : 0.45f), pick.Z);
                float back = BackOf(pick);
                var eye = target + dirXZ * back + Vector3.up * FoliageUp[range] * (scaled ? Mathf.Max(1f, pick.Size) : 1f);
                var ground = w.EntityManager.CreateEntityQuery(typeof(Ground)).GetSingleton<Ground>();
                eye.y = Mathf.Max(eye.y, ground.HeightAt(eye.x, eye.z) + (range == 2 ? 10f : 1.2f));
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                GameStatus.Lines["~aim foliage " + range] = $"{field.Library.Objects[pick.Def].Id} size {pick.Size:F1} m at {pick.X:F0},{pick.Z:F0} with {crowd} foliage in its 12 m cell, eye {back:F0} m back{(clear && !found ? ", view not clear" : "")}";
                return true;
            }
            return false;
        }

        static bool AimStructure(Camera cam, bool close)
        {
            foreach (var w in Unity.Entities.World.All)
            {
                var field = w.Name == GameSettings.ViewClient ? w.GetExistingSystemManaged<ObjectColumnsSystem>()?.Field : null;
                if (field == null) continue;
                ObjectInstance pick = default;
                float best = float.MaxValue;
                for (int cz = 0; cz < field.ChunksAcross; cz++)
                    for (int cx = 0; cx < field.ChunksAcross; cx++)
                    {
                        var chunk = field.Chunk(cx, cz);
                        foreach (var o in chunk)
                        {
                            if (!field.Library.Objects[o.Def].Parts.Exists(p => ObjectPart.IsStructure(p.Shape))) continue;
                            float score = new Vector2(o.X - home.x, o.Z - home.z).magnitude;
                            if (field.Library.Objects[o.Def].Parts[0].Shape == ObjectPart.Wall)
                            {
                                int run = 0;
                                foreach (var b in chunk)
                                    if (b.Def == o.Def && (b.X - o.X) * (b.X - o.X) + (b.Z - o.Z) * (b.Z - o.Z) < 36f * o.Size * o.Size) run++;
                                score -= run >= 4 ? 400f + 40f * run : 0f;
                            }
                            if (score < best) { best = score; pick = o; }
                        }
                    }
                if (best == float.MaxValue) return false;
                var target = new Vector3(pick.X, pick.Y + pick.Size * 0.1f, pick.Z);
                var across = new Vector3(2 * (pick.Qx * pick.Qz + pick.Qw * pick.Qy), 0, 1 - 2 * (pick.Qx * pick.Qx + pick.Qy * pick.Qy)).normalized;
                var along = new Vector3(across.z, 0, -across.x);
                float back = close ? 0.75f * pick.Size : 30f * Mathf.Max(1f, 0.25f * pick.Size);
                var eye = target + (across * 0.85f + along * 0.5f) * back + Vector3.up * (close ? 0.25f * pick.Size : 0.3f * back);
                var ground = w.EntityManager.CreateEntityQuery(typeof(Ground)).GetSingleton<Ground>();
                eye.y = Mathf.Max(eye.y, ground.HeightAt(eye.x, eye.z) + 1.4f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                GameStatus.Lines["~aim structure " + (close ? "close" : "mid")] = $"{field.Library.Objects[pick.Def].Id} size {pick.Size:F1} m at {pick.X:F0},{pick.Y:F0},{pick.Z:F0}, eye {back:F0} m back";
                return true;
            }
            return false;
        }

        static bool AimFloor(Camera cam, int mode)
        {
            foreach (var w in Unity.Entities.World.All)
            {
                var field = w.Name == GameSettings.ViewClient ? w.GetExistingSystemManaged<ObjectColumnsSystem>()?.Field : null;
                if (field == null) continue;
                var lib = field.Library.Objects;
                ObjectInstance pick = default;
                float best = float.MaxValue;
                for (int cz = 0; cz < field.ChunksAcross; cz++)
                    for (int cx = 0; cx < field.ChunksAcross; cx++)
                    {
                        var chunk = field.Chunk(cx, cz);
                        foreach (var o in chunk)
                        {
                            string id = lib[o.Def].Id;
                            if (mode < 2 ? id != "fallen-log" : id != "cliff-scree") continue;
                            int near = 0;
                            foreach (var b in chunk)
                            {
                                string bid = lib[b.Def].Id;
                                float d2 = (b.X - o.X) * (b.X - o.X) + (b.Z - o.Z) * (b.Z - o.Z);
                                if (d2 < 400f && (mode < 2 ? bid == "fallen-log" || bid == "stump" : bid == "cliff-scree")) near++;
                            }
                            float score = new Vector2(o.X - home.x, o.Z - home.z).magnitude * 0.02f - near;
                            if (score < best) { best = score; pick = o; }
                        }
                    }
                if (best == float.MaxValue) return false;
                var ground = w.EntityManager.CreateEntityQuery(typeof(Ground)).GetSingleton<Ground>();
                var trees = new System.Collections.Generic.List<Vector2>();
                float reach = mode == 0 ? 10f : 30f;
                for (int cz = 0; cz < field.ChunksAcross; cz++)
                    for (int cx = 0; cx < field.ChunksAcross; cx++)
                        foreach (var o in field.Chunk(cx, cz))
                        {
                            var sh = lib[o.Def].Parts[0].Shape;
                            if ((sh == ObjectPart.Broadleaf || sh == ObjectPart.Conifer) && Mathf.Abs(o.X - pick.X) < reach && Mathf.Abs(o.Z - pick.Z) < reach) trees.Add(new Vector2(o.X, o.Z));
                        }
                var target = new Vector3(pick.X, pick.Y + 0.3f, pick.Z);
                if (mode == 2)
                {
                    var sum = Vector3.zero;
                    int n = 0;
                    for (int cz = 0; cz < field.ChunksAcross; cz++)
                        for (int cx = 0; cx < field.ChunksAcross; cx++)
                            foreach (var o in field.Chunk(cx, cz))
                                if (lib[o.Def].Id == "cliff-scree" && Mathf.Abs(o.X - pick.X) < 20f && Mathf.Abs(o.Z - pick.Z) < 20f) { sum += new Vector3(o.X, o.Y, o.Z); n++; }
                    target = sum / n + Vector3.up * 0.3f;
                }
                float gx = ground.HeightAt(pick.X + 4f, pick.Z) - ground.HeightAt(pick.X - 4f, pick.Z), gz = ground.HeightAt(pick.X, pick.Z + 4f) - ground.HeightAt(pick.X, pick.Z - 4f);
                var up = new Vector3(gx, 0, gz);
                up = up.sqrMagnitude > 1e-4f ? up.normalized : Vector3.forward;
                float back = mode == 0 ? 4f : mode == 1 ? 16f : 26f, rise = mode == 0 ? 1.4f : mode == 1 ? 5f : 4f;
                if (mode == 2) target += Vector3.up * 3f;
                Vector3 eye = target - up * back + Vector3.up * rise;
                float clearBest = float.MinValue;
                for (int k = 0; k < 16; k++)
                {
                    float ang = k * Mathf.PI / 8f;
                    var dir = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang));
                    var e = target + dir * back + Vector3.up * rise;
                    float clear = float.MaxValue;
                    foreach (var t in trees)
                        for (int q = 1; q <= 4; q++)
                        {
                            var pq = Vector3.Lerp(target, e, q / 4f);
                            clear = Mathf.Min(clear, new Vector2(pq.x - t.x, pq.z - t.y).magnitude);
                        }
                    if (mode == 2) clear = Mathf.Min(clear, 50f) + 4f * (target.y - ground.HeightAt(e.x, e.z));
                    if (clear > clearBest + 0.5f) { clearBest = clear; eye = e; }
                }
                eye.y = Mathf.Max(eye.y, ground.HeightAt(eye.x, eye.z) + 1.4f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                GameStatus.Lines["~aim floor " + mode] = $"{lib[pick.Def].Id} size {pick.Size:F1} m at {pick.X:F0},{pick.Y:F0},{pick.Z:F0}, score {best:F1}, tree clearance {clearBest:F1} m";
                return true;
            }
            return false;
        }

        static bool AimContact(Camera cam, int mode)
        {
            foreach (var w in Unity.Entities.World.All)
            {
                var field = w.Name == GameSettings.ViewClient ? w.GetExistingSystemManaged<ObjectColumnsSystem>()?.Field : null;
                if (field == null) continue;
                var lib = field.Library.Objects;
                float best = float.MaxValue;
                Vector3 target = default, side = Vector3.forward;
                float back = 3f, rise = 1.2f;
                string what = "";
                for (int cz = 0; cz < field.ChunksAcross; cz++)
                    for (int cx = 0; cx < field.ChunksAcross; cx++)
                    {
                        var chunk = field.Chunk(cx, cz);
                        foreach (var o in chunk)
                        {
                            var def = lib[o.Def];
                            var sh = def.Parts[0].Shape;
                            float dist = new Vector2(o.X - home.x, o.Z - home.z).magnitude;
                            ObjectField.Rotate(o, 1, 0, 0, out float ux, out _, out float uz);
                            var u = new Vector3(ux, 0, uz).normalized;
                            if (mode == 0 && sh == ObjectPart.Wall)
                            {
                                float span = Structures.WallSpan * o.Size;
                                var end = new Vector3(o.X, o.Y, o.Z) + u * 0.5f * span;
                                foreach (var b in chunk)
                                {
                                    if (b.Def != o.Def || b.Id == o.Id) continue;
                                    ObjectField.Rotate(b, 1, 0, 0, out float bx, out _, out float bz);
                                    var start = new Vector3(b.X, b.Y, b.Z) - new Vector3(bx, 0, bz).normalized * 0.5f * span;
                                    float gap = new Vector2(start.x - end.x, start.z - end.z).magnitude;
                                    if (gap < 0.35f * span || gap > 0.75f * span || dist >= best) continue;
                                    best = dist;
                                    target = 0.5f * (end + start) + Vector3.up * 0.08f * o.Size;
                                    side = new Vector3(-u.z, 0, u.x) * 0.9f + u * 0.45f;
                                    back = 0.9f * o.Size; rise = 0.25f * o.Size;
                                    what = def.Id + " gate";
                                }
                            }
                            else if (mode == 1 && sh == ObjectPart.Log && dist < best)
                            {
                                best = dist;
                                target = new Vector3(o.X, o.Y + 0.2f * o.Size, o.Z);
                                side = new Vector3(-u.z, 0, u.x) + 0.3f * u;
                                back = Mathf.Max(2.2f, 1.6f * o.Size); rise = 0.9f;
                                what = def.Id;
                            }
                            else if ((mode == 2 && (sh == ObjectPart.Broadleaf || sh == ObjectPart.Conifer) || mode == 3 && sh == ObjectPart.Rock && o.Size >= 1f) && dist < best)
                            {
                                best = dist;
                                target = new Vector3(o.X, o.Y + (mode == 3 ? 0.15f * o.Size : 0.5f), o.Z);
                                side = Vector3.forward;
                                back = mode == 3 ? 1.2f * o.Size + 1.5f : 3.2f; rise = mode == 3 ? 0.4f * o.Size + 0.6f : 1.1f;
                                what = def.Id;
                            }
                        }
                    }
                if (best == float.MaxValue) return false;
                var ground = w.EntityManager.CreateEntityQuery(typeof(Ground)).GetSingleton<Ground>();
                side = side.normalized;
                Vector3 eye = target + side * back + Vector3.up * rise;
                if (mode != 0)
                {
                    float low = float.MaxValue;
                    for (int k = 0; k < 8; k++)
                    {
                        float ang = k * Mathf.PI / 4f;
                        var d = Quaternion.AngleAxis(ang * Mathf.Rad2Deg, Vector3.up) * side;
                        var e = target + d * back + Vector3.up * rise;
                        float g = ground.HeightAt(e.x, e.z);
                        if (g < low - 0.05f) { low = g; eye = e; }
                    }
                }
                eye.y = Mathf.Max(eye.y, ground.HeightAt(eye.x, eye.z) + 0.8f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                GameStatus.Lines["~aim contact " + mode] = $"{what} at {target.x:F0},{target.y:F0},{target.z:F0}, eye {back:F1} m back";
                return true;
            }
            return false;
        }

        static string FoliageBench(Camera cam, string label)
        {
            int seen = 0, total = 0;
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            foreach (var w in Unity.Entities.World.All)
            {
                var field = w.Name == GameSettings.ViewClient ? w.GetExistingSystemManaged<ObjectColumnsSystem>()?.Field : null;
                if (field == null) continue;
                var c = cam.transform.position;
                float span = WorldChunks.Size * w.GetExistingSystemManaged<ClientMapSystem>().Map.Config.CellMeters;
                for (int cz = 0; cz < field.ChunksAcross; cz++)
                    for (int cx = 0; cx < field.ChunksAcross; cx++)
                    {
                        if (Mathf.Abs((cx + 0.5f) * span - c.x) > 0.5f * span + 300f || Mathf.Abs((cz + 0.5f) * span - c.z) > 0.5f * span + 300f) continue;
                        foreach (var o in field.Chunk(cx, cz))
                        {
                            if (!field.Library.Objects[o.Def].Parts.Exists(p => ObjectPart.IsFoliage(p.Shape))) continue;
                            if ((new Vector3(o.X, o.Y, o.Z) - c).sqrMagnitude > 300f * 300f) continue;
                            total++;
                            if (GeometryUtility.TestPlanesAABB(planes, new Bounds(new Vector3(o.X, o.Y + 0.5f * o.Size, o.Z), Vector3.one * o.Size))) seen++;
                        }
                    }
            }
            int mask = cam.cullingMask;
            double[] off = new double[3], on = new double[3];
            for (int k = 0; k < 3; k++)
            {
                cam.cullingMask = mask & ~(1 << ObjectViewSystem.FoliageLayer);
                Shader.SetGlobalFloat("_StabbyFoliageOff", 1);
                off[k] = BenchAt(cam, 1920, 1080);
                cam.cullingMask = mask;
                Shader.SetGlobalFloat("_StabbyFoliageOff", 0);
                on[k] = BenchAt(cam, 1920, 1080);
            }
            double a = off.Min(), b = on.Min();
            return $"bench {label} 1080p (ms, foliage culled/drawn, best of 3): {a:F2}/{b:F2}, foliage {b - a:F2} ms; {seen} of {total} foliage objects within 300 m in the frustum";
        }

        static bool AimBoulder(Camera cam, bool close)
        {
            foreach (var w in Unity.Entities.World.All)
            {
                var q = w.EntityManager.CreateEntityQuery(typeof(Ground));
                if (q.CalculateEntityCount() != 1) continue;
                var ground = q.GetSingleton<Ground>();
                ref var g = ref ground.Blob.Value;
                int at = -1;
                for (int k = 0; k < g.Edits.Length; k++)
                {
                    var e = g.Edits[k];
                    if (e.Shape == FeatureEdit.Boulder && (e.Ax != e.Bx || e.Az != e.Bz) && (at < 0 || e.R > g.Edits[at].R)) at = k;
                }
                if (at < 0) { GameStatus.Lines["~aim boulder " + (close ? "close" : "mid")] = $"{w.Name}: no boulder among {g.Edits.Length} edits"; return false; }
                var b = g.Edits[at];
                var top = new Vector3(0.5f * (b.Ax + b.Bx), 0.5f * (b.Ay + b.By) + b.R, 0.5f * (b.Az + b.Bz));
                var side = new Vector3(-(b.Bz - b.Az), 0, b.Bx - b.Ax).normalized;
                float dist = close ? 4f * b.R : 120f;
                var eye = top + side * dist + Vector3.up * (close ? 1.5f * b.R : 25f);
                eye.y = Mathf.Max(eye.y, ground.HeightAt(eye.x, eye.z) + 2f);
                var target = top - Vector3.up * b.R * 0.6f;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                GameStatus.Lines["~aim boulder " + (close ? "close" : "mid")] = $"edit {at} R {b.R:F1} at {top.x:F0},{top.y:F0},{top.z:F0} from {dist:F0} m";
                return true;
            }
            return false;
        }

        static bool Coast(Camera cam, float height)
        {
            foreach (var w in Unity.Entities.World.All)
            {
                var map = w.GetExistingSystemManaged<ClientMapSystem>()?.Map;
                if (map == null) continue;
                int n = map.Config.Size, best = -1;
                float cm = map.Config.CellMeters, bestD = float.MaxValue;
                for (int z = 1; z < n - 1; z++)
                    for (int x = 1; x < n - 1; x++)
                    {
                        int i = z * n + x;
                        if (!map.Sea[i] || (map.Sea[i + 1] && map.Sea[i - 1] && map.Sea[i + n] && map.Sea[i - n])) continue;
                        float d = (new Vector2(x * cm, z * cm) - new Vector2(home.x, home.z)).sqrMagnitude;
                        if (d < bestD) { bestD = d; best = i; }
                    }
                if (best < 0) return false;
                var target = new Vector3(best % n * cm, 0, best / n * cm);
                var eye = target + new Vector3(-0.5f, 0, -0.4f) * height + Vector3.up * height;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                GameStatus.Lines["~aim coast " + height] = $"coast at {target.x:F0},{target.z:F0} from {height:F0} m, {Mathf.Sqrt(bestD):F0} m from the player";
                return true;
            }
            return false;
        }

        static bool FarCoast(Camera cam, float back, float height)
        {
            foreach (var w in Unity.Entities.World.All)
            {
                var map = w.GetExistingSystemManaged<ClientMapSystem>()?.Map;
                if (map == null) continue;
                int n = map.Config.Size, r = 12, best = -1;
                float cm = map.Config.CellMeters, bestD = float.MaxValue;
                Vector2 bestDir = default;
                for (int z = r; z < n - r; z++)
                    for (int x = r; x < n - r; x++)
                    {
                        int i = z * n + x;
                        if (!map.Sea[i] || (map.Sea[i + 1] && map.Sea[i - 1] && map.Sea[i + n] && map.Sea[i - n])) continue;
                        float d = (new Vector2(x * cm, z * cm) - new Vector2(home.x, home.z)).sqrMagnitude;
                        if (d >= bestD) continue;
                        var dir = Vector2.zero;
                        int sea = 0;
                        for (int dz = -r; dz <= r; dz++)
                            for (int dx = -r; dx <= r; dx++)
                                if (map.Sea[(z + dz) * n + x + dx]) { dir += new Vector2(dx, dz); sea++; }
                        if (sea < r * r || dir.sqrMagnitude < 1) continue;
                        bestD = d;
                        best = i;
                        bestDir = dir.normalized;
                    }
                if (best < 0) return false;
                var target = new Vector3(best % n * cm, 0, best / n * cm);
                var eye = target + new Vector3(bestDir.x, 0, bestDir.y) * back + Vector3.up * height;
                eye.y = Mathf.Max(eye.y, GroundHeight(eye.x, eye.z) + height);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target + Vector3.up * 20f - eye));
                GameStatus.Lines["~aim coast far"] = $"far coast at {target.x:F0},{target.z:F0} from {back:F0} m out, {height:F0} m up";
                return true;
            }
            return false;
        }

        static Vector3 home;
        static readonly string[] CloseNames = { "rock", "grass", "sand", "snow" };

        static readonly string[] BiomeShots = { "tundra", "alpine", "desert", "cold-desert", "savanna", "steppe" };

        static readonly string[] BeachShots = { "straight", "bay", "point" };

        static bool AimBeach(Camera cam, int kind, bool mid)
        {
            foreach (var w in Unity.Entities.World.All)
            {
                var map = w.GetExistingSystemManaged<ClientMapSystem>()?.Map;
                var q = w.EntityManager.CreateEntityQuery(typeof(Ground));
                if (map == null || q.CalculateEntityCount() != 1) continue;
                var ground = q.GetSingleton<Ground>();
                int n = map.Config.Size, r = 5, best = -1;
                float cm = map.Config.CellMeters, bestD = float.MaxValue;
                Vector2 bestDir = default;
                for (int z = r; z < n - r; z++)
                    for (int x = r; x < n - r; x++)
                    {
                        int i = z * n + x;
                        if (map.Sea[i] || map.Height[i] > 1.6f || !(map.Sea[i + 1] || map.Sea[i - 1] || map.Sea[i + n] || map.Sea[i - n])) continue;
                        float d = (new Vector2(x * cm, z * cm) - new Vector2(home.x, home.z)).sqrMagnitude;
                        if (d >= bestD) continue;
                        var dir = Vector2.zero;
                        int sea = 0, low = 0;
                        for (int dz = -r; dz <= r; dz++)
                            for (int dx = -r; dx <= r; dx++)
                            {
                                int j = (z + dz) * n + x + dx;
                                if (map.Sea[j]) { dir += new Vector2(dx, dz); sea++; }
                                else if (map.Height[j] < 4f) low++;
                            }
                        float f = sea / (float)((2 * r + 1) * (2 * r + 1));
                        bool ok = kind == 0 ? f > 0.42f && f < 0.58f && dir.magnitude > 0.8f * sea * r * 0.5f : kind == 1 ? f > 0.18f && f < 0.34f : f > 0.68f && f < 0.85f;
                        if (!ok || low < (2 * r + 1) * (2 * r + 1) - sea - 12 || dir.sqrMagnitude < 1) continue;
                        bestD = d; best = i; bestDir = dir.normalized;
                    }
                if (best < 0) return false;
                var target = new Vector3(best % n * cm, 0, best / n * cm);
                target.y = ground.HeightAt(target.x, target.z);
                var sea3 = new Vector3(bestDir.x, 0, bestDir.y);
                var along = new Vector3(-bestDir.y, 0, bestDir.x);
                Vector3 eye, aim;
                if (mid)
                {
                    eye = target - sea3 * 26f - along * 12f;
                    eye.y = Mathf.Max(target.y, ground.HeightAt(eye.x, eye.z)) + 13f;
                    aim = target + sea3 * 2f;
                }
                else
                {
                    eye = target - sea3 * 5f - along * 3f;
                    eye.y = ground.HeightAt(eye.x, eye.z) + 1.8f;
                    aim = target + along * 4f + sea3 * 1f;
                    aim.y = ground.HeightAt(aim.x, aim.z);
                }
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim - eye));
                GameStatus.Lines["~aim beach " + kind] = $"{BeachShots[kind]} beach at {target.x:F0},{target.y:F1},{target.z:F0}, sea toward {bestDir.x:F2},{bestDir.y:F2}, {Mathf.Sqrt(bestD):F0} m from the player";
                return true;
            }
            return false;
        }

        static readonly string[] ForestShots = { "temperate-forest", "boreal", "temperate-rain" };

        static bool AimForest(Camera cam, string id, int view)
        {
            var near = cam.transform.position + new Vector3(0.77f, 0, 0.64f) * 24f;
            foreach (var w in Unity.Entities.World.All)
            {
                var field = w.Name == GameSettings.ViewClient ? w.GetExistingSystemManaged<ObjectColumnsSystem>()?.Field : null;
                if (field == null) continue;
                var map = w.GetExistingSystemManaged<ClientMapSystem>().Map;
                var ground = w.EntityManager.CreateEntityQuery(typeof(Ground)).GetSingleton<Ground>();
                int b = System.Array.IndexOf(map.BiomeIds, id), n = map.Config.Size, across = field.ChunksAcross;
                float cm = map.Config.CellMeters, span = WorldChunks.Size * cm;
                var trees = new System.Collections.Generic.List<Vector2>();
                int ncx = (int)(near.x / span), ncz = (int)(near.z / span);
                for (int cz = Mathf.Max(0, ncz - 1); cz <= Mathf.Min(across - 1, ncz + 1); cz++)
                    for (int cx = Mathf.Max(0, ncx - 1); cx <= Mathf.Min(across - 1, ncx + 1); cx++)
                        foreach (var o in field.Chunk(cx, cz))
                        {
                            bool tree = false;
                            foreach (var part in field.Library.Objects[o.Def].Parts) tree |= part.Shape == ObjectPart.Broadleaf || part.Shape == ObjectPart.Conifer;
                            int gx = Mathf.Clamp((int)(o.X / cm), 0, n - 1), gz = Mathf.Clamp((int)(o.Z / cm), 0, n - 1);
                            if (tree && map.Biome[gz * n + gx] == b && (new Vector2(o.X - near.x, o.Z - near.z)).sqrMagnitude < 360000f) trees.Add(new Vector2(o.X, o.Z));
                        }
                if (trees.Count == 0) return false;
                int best = -1;
                float score = float.MinValue;
                for (int k = 0; k < trees.Count; k += Mathf.Max(1, trees.Count / 1500))
                {
                    int count = 0;
                    foreach (var t in trees) if ((t - trees[k]).sqrMagnitude < 400f) count++;
                    float hx = ground.HeightAt(trees[k].x + 6, trees[k].y) - ground.HeightAt(trees[k].x - 6, trees[k].y), hz = ground.HeightAt(trees[k].x, trees[k].y + 6) - ground.HeightAt(trees[k].x, trees[k].y - 6);
                    float sc = count - 0.002f * (trees[k] - new Vector2(near.x, near.z)).magnitude - 2f * Mathf.Sqrt(hx * hx + hz * hz);
                    if (sc > score) { score = sc; best = k; }
                }
                var c = trees[best];
                Vector3 dir = Vector3.zero, target = Vector3.zero, eye = Vector3.zero;
                float clear = -1;
                for (int a = 0; a < 16; a++)
                {
                    var d = Quaternion.Euler(0, a * 22.5f, 0) * new Vector3(0.77f, 0, 0.64f);
                    var t = new Vector3(c.x, 0, c.y) + new Vector3(-d.z, 0, d.x) * 3f;
                    var e = t - d * (view == 2 ? 48f : view == 1 ? 22f : 3.5f);
                    float m = float.MaxValue;
                    foreach (var tr in trees)
                        for (float s = 0; s <= 1; s += 0.25f)
                            m = Mathf.Min(m, (tr - Vector2.Lerp(new Vector2(e.x, e.z), new Vector2(t.x, t.z), s)).sqrMagnitude);
                    if (m > clear) { clear = m; dir = d; target = t; eye = e; }
                }
                eye.y = ground.HeightAt(eye.x, eye.z) + (view == 2 ? 11f : view == 1 ? 5f : 1.6f);
                var aim = target + dir * (view == 2 ? 6f : view == 1 ? 4f : 1.5f);
                aim.y = ground.HeightAt(aim.x, aim.z);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim - eye));
                GameStatus.Lines["~aim forest " + id] = $"{id} stand at {c.x:F0},{c.y:F0}, {trees.Count} trees searched, score {score:F1}";
                return true;
            }
            return false;
        }

        static bool AimBiome(Camera cam, string id)
        {
            foreach (var w in Unity.Entities.World.All)
            {
                var sys = w.GetExistingSystemManaged<ClientMapSystem>();
                var map = sys?.Map;
                var q = w.EntityManager.CreateEntityQuery(typeof(Ground));
                if (map == null || map.Biome == null || q.CalculateEntityCount() != 1) continue;
                var ground = q.GetSingleton<Ground>();
                int n = map.Config.Size, best = -1, b = System.Array.IndexOf(map.BiomeIds, id), r = 2, cells = 0;
                if (b < 0) return false;
                float cm = map.Config.CellMeters, bestD = float.MaxValue, anyD = float.MaxValue;
                int any = -1;
                for (int z = r; z < n - r; z++)
                    for (int x = r; x < n - r; x++)
                    {
                        int i = z * n + x;
                        if (map.Biome[i] != b || map.Sea[i]) continue;
                        cells++;
                        float da = (new Vector2(x * cm, z * cm) - new Vector2(home.x, home.z)).sqrMagnitude;
                        if (da < anyD) { anyD = da; any = i; }
                        if (map.Biome[i - r] != b || map.Biome[i + r] != b || map.Biome[i - r * n] != b || map.Biome[i + r * n] != b) continue;
                        float gx = (map.Height[i + 1] - map.Height[i - 1]) / (2 * cm), gz = (map.Height[i + n] - map.Height[i - n]) / (2 * cm);
                        if (gx * gx + gz * gz > 0.0144f) continue;
                        float d = (new Vector2(x * cm, z * cm) - new Vector2(home.x, home.z)).sqrMagnitude;
                        if (d < bestD) { bestD = d; best = i; }
                    }
                GameStatus.Lines["~aim biome " + id] = $"{id}: {cells} land cells, none flat";
                if (best < 0) { best = any; bestD = anyD; }
                if (best < 0) return false;
                var target = new Vector3(best % n * cm, 0, best / n * cm);
                target.y = ground.HeightAt(target.x, target.z);
                var eye = target + new Vector3(-0.77f, 0, -0.64f) * 24f;
                eye.y = Mathf.Max(target.y, ground.HeightAt(eye.x, eye.z)) + 7f;
                var aim = target + new Vector3(0.77f, 0, 0.64f) * 20f;
                aim.y = Mathf.Max(ground.HeightAt(aim.x, aim.z), eye.y - 10f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim - eye));
                GameStatus.Lines["~aim biome " + id] = $"{id} at {target.x:F0},{target.y:F0},{target.z:F0}, {Mathf.Sqrt(bestD):F0} m from the player";
                return true;
            }
            return false;
        }

        static void SetSmooth(bool on)
        {
            foreach (var w in Unity.Entities.World.All)
            {
                var sys = w.Name == GameSettings.ViewClient ? w.GetExistingSystemManaged<TerrainViewSystem>() : null;
                if (sys != null) sys.SmoothOnly = on;
            }
        }

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
                        bool ok = which == 0 ? s > 0.8f : which == 2 && map.Height[i] < 1.2f ? s < 0.1f : s < 0.12f && map.Height[i] >= 3 && map.Biome != null && map.Biome[i] >= 0 && sys.Materials.Surface.CoatOf(map.BiomeIds[map.Biome[i]]) == (which == 1 ? 0 : which);
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

        public static float Sedimentary(float off) => MaterialLibrary.Sedimentary(off);

        static System.Collections.IEnumerator CliffTour(Camera cam, string dir)
        {
            ClientMapSystem sys = null;
            Ground ground = default;
            foreach (var w in Unity.Entities.World.All)
            {
                var s = w.GetExistingSystemManaged<ClientMapSystem>();
                var q = w.EntityManager.CreateEntityQuery(typeof(Ground));
                if (s?.Map == null || s.Materials == null || q.CalculateEntityCount() != 1) continue;
                sys = s;
                ground = q.GetSingleton<Ground>();
                break;
            }
            if (sys == null) { GameStatus.Lines["~cliffs"] = "no map"; yield break; }
            var map = sys.Map;
            var offs = sys.Materials.Offsets(map.Config);
            int n = map.Config.Size;
            float cm = map.Config.CellMeters;
            var picks = new int[] { -1, -1 };
            var downs = new Vector2[2];
            var dist = new float[] { float.MaxValue, float.MaxValue };
            for (int z = 2; z < n - 2; z++)
                for (int x = 2; x < n - 2; x++)
                {
                    int i = z * n + x;
                    if (map.Sea[i] || map.Height[i] < 3) continue;
                    float gx = (map.Height[i + 1] - map.Height[i - 1]) / (2 * cm), gz = (map.Height[i + n] - map.Height[i - n]) / (2 * cm), s = Mathf.Sqrt(gx * gx + gz * gz);
                    if (s < 1.1f) continue;
                    float sed = Sedimentary(offs[i]);
                    int k = sed > 0.9f ? 0 : sed < 0.1f ? 1 : -1;
                    if (k < 0) continue;
                    float d = (new Vector2(x * cm, z * cm) - new Vector2(home.x, home.z)).sqrMagnitude;
                    if (d < dist[k]) { dist[k] = d; picks[k] = i; downs[k] = new Vector2(-gx, -gz).normalized; }
                }
            string[] kinds = { "sed", "massive" };
            float[] back = { 3.5f, 14f, 50f, 250f }, up = { 0.6f, 2f, 12f, 60f };
            string[] range = { "close", "near", "mid", "far" };
            for (int k = 0; k < 2; k++)
            {
                if (picks[k] < 0) { GameStatus.Lines["~cliff " + kinds[k]] = "no " + kinds[k] + " cliff found"; continue; }
                var target = new Vector3(picks[k] % n * cm, 0, picks[k] / n * cm);
                target.y = ground.HeightAt(target.x, target.z);
                var dn = new Vector3(downs[k].x, 0, downs[k].y);
                GameStatus.Lines["~cliff " + kinds[k]] = $"{kinds[k]} cliff at {target.x:F0},{target.y:F0},{target.z:F0}, offset {offs[picks[k]]:F1}, {Mathf.Sqrt(dist[k]):F0} m from the player";
                for (int r = 0; r < 4; r++)
                {
                    var eye = target + dn * back[r] + Vector3.up * up[r];
                    eye.y = Mathf.Max(eye.y, ground.HeightAt(eye.x, eye.z) + 1.5f);
                    var look = target + Vector3.up * (r == 0 ? 1.5f : r == 3 ? 0f : 4f);
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
                    for (var w = Settle(180); w.MoveNext();) yield return null;
                    Shot(cam, null, "warmup");
                    Shot(cam, dir, "cliff-" + kinds[k] + "-" + range[r]);
                }
                var crest = target;
                for (float s = 5f; s < 600f; s += 5f)
                {
                    var p = target - dn * s;
                    p.y = ground.HeightAt(p.x, p.z);
                    if (p.y < crest.y + 1f && s > 30f) break;
                    if (p.y > crest.y) crest = p;
                }
                float[] sky = { 120f, 400f };
                string[] skyName = { "skyline-mid", "skyline-far" };
                for (int r = 0; r < 2; r++)
                {
                    var eye = Vector3.zero;
                    float best = float.MaxValue;
                    for (int a = -6; a <= 6; a++)
                    {
                        var e = crest + Quaternion.Euler(0, a * 12f, 0) * dn * sky[r];
                        float g = ground.HeightAt(e.x, e.z) + 1.8f;
                        e.y = g;
                        for (float t = 0.03f; t < 0.95f; t += 0.01f)
                        {
                            float px = e.x + (crest.x - e.x) * t, pz = e.z + (crest.z - e.z) * t;
                            e.y = Mathf.Max(e.y, (ground.HeightAt(px, pz) + 1.5f - t * crest.y) / (1 - t));
                        }
                        float cost = e.y - g + Mathf.Abs(a) * 2f + Mathf.Max(0, e.y - crest.y + 5f) * 10f;
                        if (cost < best) { best = cost; eye = e; }
                    }
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(crest + Vector3.up * 2f - eye));
                    for (var w = Settle(180); w.MoveNext();) yield return null;
                    Shot(cam, null, "warmup");
                    Shot(cam, dir, "cliff-" + kinds[k] + "-" + skyName[r]);
                }
            }
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
            if (GameStatus.Line("terrain", out var t)) sb.Append("; ").Append(t);
            return sb.ToString();
        }

        static readonly System.Collections.Generic.List<(string, Vector3, Quaternion)> poses = new System.Collections.Generic.List<(string, Vector3, Quaternion)>(), lows = new System.Collections.Generic.List<(string, Vector3, Quaternion)>();
        static System.Collections.IEnumerator job;
        static bool castDone;

        static System.Collections.IEnumerator Settle(int frames)
        {
            for (int k = 0; k < frames || (GameStatus.TerrainBehind > 0 && k < 1800); k++) yield return null;
        }

        static System.Collections.IEnumerator CastTour(Camera cam, string dir)
        {
            foreach (var (label, pos, rot) in poses)
            {
                cam.transform.SetPositionAndRotation(pos, rot);
                for (var w = Settle(180); w.MoveNext();) yield return null;
                var ms = new double[2];
                for (int k = 0; k < 4; k++)
                {
                    TerrainViewSystem.Casting = (k & 1) == 1;
                    for (var w = Settle(4); w.MoveNext();) yield return null;
                    ms[k & 1] += BenchAt(cam, 1920, 1080) / 2;
                }
                GameStatus.Line("terrain", out var t);
                GameStatus.Lines["~bench " + label + " terrain casting"] = $"bench {label} 1080p terrain casting off/on (ms, full): {ms[0]:F2}/{ms[1]:F2}; {t}";
                var urp = (UniversalRenderPipelineAsset)UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
                bool keepCopy = urp.supportsCameraOpaqueTexture;
                var copy = new double[2];
                for (int k = 0; k < 8; k++)
                {
                    urp.supportsCameraOpaqueTexture = (k & 1) == 1;
                    for (var w = Settle(4); w.MoveNext();) yield return null;
                    copy[k & 1] += BenchAt(cam, 1920, 1080) / 4;
                }
                urp.supportsCameraOpaqueTexture = keepCopy;
                GameStatus.Lines["~bench " + label + " opaque copy"] = $"bench {label} 1080p opaque copy off/on (ms, full): {copy[0]:F2}/{copy[1]:F2}";
                var sunk = new double[4];
                for (int k = 0; k < 8; k++)
                {
                    TerrainViewSystem.HideSunk = (k & 1) == 1;
                    for (var w = Settle(4); w.MoveNext();) yield return null;
                    sunk[k & 1] += BenchAt(cam, 1920, 1080) / 4;
                    sunk[2 + (k & 1)] += BenchAt(cam, 3840, 2160) / 4;
                }
                GameStatus.Line("terrain", out t);
                GameStatus.Lines["~bench " + label + " sunk nodes"] = $"bench {label} sunk nodes drawn/hidden (ms, full): 1080p {sunk[0]:F2}/{sunk[1]:F2}, 4K {sunk[2]:F2}/{sunk[3]:F2}; {t}";
            }
            var sun = Object.FindFirstObjectByType<Light>();
            var keep = sun.transform.rotation;
            foreach (var (name, pos, rot) in lows)
            {
                cam.transform.SetPositionAndRotation(pos, rot);
                sun.transform.rotation = name == "rock" ? keep : Quaternion.Euler(name == "grass" ? 6f : 14f, rot.eulerAngles.y + (name == "grass" ? 160f : 100f), 0);
                for (var w = Settle(180); w.MoveNext();) yield return null;
                string[] tags = name == "rock" ? new[] { "-nocast", "-cast", "-detailoff", "-nojoints" } : new[] { "-low-nocast", "-low", "-low-detailoff", "-low-nojoints" };
                var rockB = Shader.GetGlobalVector("_StabbyRockB");
                for (int c = 0; c < (name == "cliff" || name == "rock" ? tags.Length : 2); c++)
                {
                    TerrainViewSystem.Casting = c > 0;
                    bool rock = name == "rock";
                    Shader.SetGlobalFloat("_StabbyDetailOff", c == 2 ? 1 : 0);
                    for (var w = Settle(4); w.MoveNext();) yield return null;
                    Shader.SetGlobalVector("_StabbyRockB", c == 3 ? new Vector4(rockB.x, rockB.y, rockB.z, 0) : rockB);
                    Shot(cam, null, "warmup");
                    Shot(cam, dir, (rock ? "close-rock" : name) + tags[c]);
                }
                Shader.SetGlobalFloat("_StabbyDetailOff", 0);
                Shader.SetGlobalVector("_StabbyRockB", rockB);
            }
            sun.transform.rotation = keep;
            TerrainViewSystem.Casting = true;
        }

        static string ShadowBench(Camera cam, string label, bool blades)
        {
            poses.Add((label, cam.transform.position, cam.transform.rotation));
            var sun = Object.FindFirstObjectByType<Light>();
            var modes = new[] { LightShadows.None, LightShadows.Soft };
            var sb = new System.Text.StringBuilder($"bench {label} 1080p shadows off/on (ms, full):");
            for (int pass = 0; pass < (blades ? 2 : 1); pass++)
            {
                GrassViewSystem.Paused = pass == 1;
                var ms = new double[2];
                for (int k = 0; k < 4; k++)
                {
                    sun.shadows = modes[k & 1];
                    ms[k & 1] += BenchAt(cam, 1920, 1080) / 2;
                }
                sb.Append(pass == 0 ? " blades on " : ", blades off ").Append($"{ms[0]:F2}/{ms[1]:F2}");
            }
            GrassViewSystem.Paused = false;
            sun.shadows = LightShadows.Soft;
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
