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
    public static partial class GameCapture
    {
        const string Key = "Stabby.GameCapture", StartKey = "Stabby.GameCapture.Start", ScenePath = "Assets/Scenes/Game.unity";
        const double TimeoutSeconds = 900;
        static int settle = -1, phase, wait, smooth, chaseWait, impStep, firstStep;
        static readonly string only = System.Environment.GetEnvironmentVariable("STABBY_CAPTURE_ONLY");
        static readonly string variantOnly = System.Environment.GetEnvironmentVariable("STABBY_CAPTURE_VARIANT");
        static bool bank, lake;
        const float UndercutMaxR = 25f;
        static double playAt = -1;

        static GameCapture()
        {
            if (SessionState.GetBool(Key, false)) EditorApplication.update += Poll;
            if (Application.isBatchMode) SkipSearchIndexing();
        }

        static int searchPolls;

        static void SkipSearchIndexing()
        {
            EditorApplication.update -= SkipSearchIndexing;
            if (++searchPolls > 300) return;
            foreach (var d in EditorApplication.delayCall?.GetInvocationList() ?? System.Array.Empty<System.Delegate>())
                if (d.Method.Name == "IndexationOnStartup")
                {
                    EditorApplication.delayCall -= (EditorApplication.CallbackFunction)d;
                    return;
                }
            EditorApplication.update += SkipSearchIndexing;
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
            var mist = AssetDatabase.LoadAssetAtPath<Material>("Assets/Viewer/Mist.mat");
            if (mist == null)
            {
                mist = new Material(Shader.Find("Stabby/FallMist"));
                AssetDatabase.CreateAsset(mist, "Assets/Viewer/Mist.mat");
            }
            game.MistMaterial = mist;

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
            PinTime();
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
        static (Vector3 pos, Quaternion rot) farPose;

        static void PollBody()
        {
            if (EditorApplication.timeSinceStartup - SessionState.GetFloat(StartKey, 0) - LoadWaitTotal() > (only == "creaturefar" || only == "detail" ? 2 * TimeoutSeconds : TimeoutSeconds))
            {
                Debug.LogError("GameCapture: timed out\n" + Status());
                Finish(2);
                return;
            }
            if (!EditorApplication.isPlaying) return;
            if (playAt < 0) playAt = EditorApplication.timeSinceStartup;
            if (!quiet) quiet = QuietBatchWarning();
            SpikeProbe.Tick();
            SystemCost.Tick();
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
            if (phase == 0 && Loading()) { if (firstStep == 0) chaseWait++; return; }

            if (dir == null) { dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "out", "unity", "game")); Directory.CreateDirectory(dir); }
            var cam = Camera.main;
            if (phase == 0 && firstStep == 0)
            {
                Shot(cam, null, "warmup");
                if (chaseWait > 0) GameStatus.Lines["~chase wait"] = $"chase waited {chaseWait} extra frames for terrain and objects";
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
                GameStatus.HoldCamera = true;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
                farPose = (home + new Vector3(0, 90, 0), Quaternion.LookRotation(fwd - new Vector3(0, 0.25f, 0)));
                firstStep = 1;
                return;
            }
            if (phase == 0 && firstStep == 1)
            {
                Shot(cam, dir, "overhead");
                cam.transform.SetPositionAndRotation(farPose.pos, farPose.rot);
                firstStep = 2;
                return;
            }
            if (phase == 0)
            {
                Shot(cam, dir, "far");
                GameStatus.Lines["~bench far"] = Bench(cam, "far");
                GameStatus.Lines["~bench far blades"] = GrassViewSystem.Counts();
                GameStatus.Lines["~bench far shadows"] = ShadowBench(cam, "far", true);
                GameStatus.HoldCamera = true;
                phase = only == "arch" ? 1 : only == "cliffs" ? 50 : only == "scale" ? 120 : only == "rock" ? 7 : only == "coast" ? 9 : only == "grass" ? 8 : only == "biomes" ? 9 : only == "beach" ? 40 : only == "forest" ? 60 : only == "river" ? 80 : only == "water" ? 140 : only == "structure" ? 24 : only == "floor" ? 26 + BiomeShots.Length : only == "inn" ? 160 : only == "discovery" ? 180 : only == "camp" ? 190 : only == "hermit" ? 230 : only == "shrine" ? 240 : only == "ruin" ? 260 : only == "spring" ? 270 : only == "vents" ? 320 : only == "bog" ? 330 : only == "mesas" ? 340 : only == "creatures" ? 200 : only == "creaturebench" ? 250 : only == "landforms" ? 220 : only == "creaturefar" ? 280 : only == "fastwalk" ? 290 : only == "species" ? 310 : only == "fairyring" ? 300 : only == "wolfden" ? 400 : only == "controls" ? 350 : only == "terrainfix" ? 360 : only == "rockbeds" ? 370 : only == "liquids" ? 380 : only == "watertint" ? 390 : only == "brainbench" ? 420 : only == "desires" ? 430 : only == "rivermouth" ? 440 : only == "waterfall" ? 450 : only == "siting" ? 460 : only == "shadowtiers" ? 470 : only == "sprayshadows" ? 480 : only == "farhills" ? 490 : only == "detail" ? 500 : only == "geyserpool" ? 510 : only == "daynight" ? 520 : only == "lightbudget" ? 540 : only == "waterlook" ? 550 : only == "lightreceivers" ? 560 : only == "firelight" ? 570 : only == "caves" ? 580 : only == "lightscale" ? 590 : only == "oceandetail" ? 600 : only == "waterlights" ? 610 : only == "creaturememory" ? 620 : only == "rigdump" ? 630 : only == "creatureskin" ? 640 : only == "cavemouth" ? 650 : only == "cavelights" ? 660 : only == "caveice" ? 670 : only == "dungeon" ? 680 : only == "dungeons" ? 690 : only == "mines" ? 700 : only == "dungeonlinks" ? 710 : only == "towns" ? 720 : only == "roads" ? 730 : only == "cavelava" ? 740 : only == "cavefalls" ? 750 : only == "overlook" ? 760 : only == "wallheads" ? 770 : only == "bones" ? 820 : only == "creaturesyoung" ? 900 : only == "boulder" ? 780 : only == "salttower" ? 800 : only == "coastfall" ? 980 : 1;
            }
            while (phase >= 160 && phase < 160 + InnShots.Length)
            {
                string name = InnShots[phase - 160];
                if (wait == 0)
                {
                    if (!AimInn(cam, phase - 160)) { GameStatus.Lines["~" + name] = "no inn placed"; phase++; continue; }
                    wait = 180;
                    return;
                }
                if (--wait > 0 || Loading()) return;
                Shot(cam, null, "warmup");
                Shot(cam, dir, name);
                if (phase == 161) GameStatus.Lines["~bench inn"] = Bench(cam, "inn mid");
                wait = 0;
                phase++;
            }
            while (phase >= 180 && phase < 180 + RingShots.Length)
            {
                string name = RingShots[phase - 180];
                if (wait == 0)
                {
                    if (!AimRing(cam, phase - 180)) { GameStatus.Lines["~" + name] = "no stone ring placed"; phase++; continue; }
                    wait = 180;
                    return;
                }
                if (--wait > 0 || Loading()) return;
                Shot(cam, null, "warmup");
                Shot(cam, dir, name);
                if (phase == 181) GameStatus.Lines["~bench ring"] = Bench(cam, "stone ring mid");
                wait = 0;
                phase++;
            }
            while (phase >= 190 && phase < 190 + CampShots.Length)
            {
                string name = CampShots[phase - 190];
                if (wait == 0)
                {
                    if (!AimCamp(cam, phase - 190)) { GameStatus.Lines["~" + name] = "no campsite placed"; phase++; continue; }
                    wait = 180;
                    return;
                }
                if (--wait > 0 || Loading()) return;
                Shot(cam, null, "warmup");
                Shot(cam, dir, name);
                if (phase == 191) GameStatus.Lines["~bench camp"] = Bench(cam, "campsite mid");
                wait = 0;
                phase++;
            }
            while (phase >= 230 && phase < 230 + HermitShots.Length)
            {
                string name = HermitShots[phase - 230];
                if (wait == 0)
                {
                    if (!AimSite(cam, phase - 230, ObjectPart.Hermitage, Structures.HermitU, HermitShots, "hermitage", new Vector3(0.97f, -0.25f, 5f), Structures.HutZ)) { GameStatus.Lines["~" + name] = "no hermitage placed"; phase++; continue; }
                    wait = 180;
                    return;
                }
                if (--wait > 0 || Loading()) return;
                Shot(cam, null, "warmup");
                Shot(cam, dir, name);
                if (phase == 231) GameStatus.Lines["~bench hermit"] = Bench(cam, "hermitage mid");
                wait = 0;
                phase++;
            }
            while (phase >= 240 && phase < 240 + ShrineShots.Length)
            {
                string name = ShrineShots[phase - 240];
                if (wait == 0)
                {
                    if (!AimSite(cam, phase - 240, ObjectPart.Wayside, Structures.WaysideU, ShrineShots, "wayside shrine", new Vector3(0.92f, 0.4f, 4.6f), -1f)) { GameStatus.Lines["~" + name] = "no wayside shrine placed"; phase++; continue; }
                    wait = 180;
                    return;
                }
                if (--wait > 0 || Loading()) return;
                Shot(cam, null, "warmup");
                Shot(cam, dir, name);
                if (phase == 241) GameStatus.Lines["~bench shrine"] = Bench(cam, "wayside mid");
                wait = 0;
                phase++;
            }
            while (phase >= 260 && phase < 260 + RuinShots.Length)
            {
                string name = RuinShots[phase - 260];
                if (wait == 0)
                {
                    if (!AimSite(cam, phase - 260, ObjectPart.Ruin, Structures.RuinU, RuinShots, "ruin", new Vector3(0.95f, 0.35f, 8f), 0f)) { GameStatus.Lines["~" + name] = "no ruin placed"; phase++; continue; }
                    wait = 180;
                    return;
                }
                if (--wait > 0 || Loading()) return;
                Shot(cam, null, "warmup");
                Shot(cam, dir, name);
                if (phase == 261) GameStatus.Lines["~bench ruin"] = Bench(cam, "ruin mid");
                wait = 0;
                phase++;
            }
            while (phase >= 270 && phase < 270 + SpringShots.Length)
            {
                string name = SpringShots[phase - 270];
                if (wait == 0)
                {
                    if (!AimSite(cam, phase - 270, ObjectPart.HotSpring, Structures.SpringU, SpringShots, "hot spring", new Vector3(0.85f, 0.5f, 2.2f), 0f)) { GameStatus.Lines["~" + name] = "no hot spring placed"; phase++; continue; }
                    wait = 180;
                    return;
                }
                if (--wait > 0 || Loading()) return;
                Shot(cam, null, "warmup");
                Shot(cam, dir, name);
                if (phase == 271)
                {
                    GameStatus.Lines["~bench spring"] = Bench(cam, "hot spring");
                    GameStatus.Lines["~bench spring steam"] = BenchMist(cam, "hot spring");
                }
                wait = 0;
                phase++;
            }
            while (phase >= 300 && phase < 300 + FairyShots.Length)
            {
                string name = FairyShots[phase - 300];
                if (wait == 0)
                {
                    if (!AimSite(cam, phase - 300, ObjectPart.FairyRing, Structures.FairyU, FairyShots, "fairy ring", new Vector3(0.95f, 0.35f, 5.5f), 0f)) { GameStatus.Lines["~" + name] = "no fairy ring placed"; phase++; continue; }
                    wait = 180;
                    return;
                }
                if (--wait > 0 || Loading()) return;
                Shot(cam, null, "warmup");
                Shot(cam, dir, name);
                if (phase == 301) GameStatus.Lines["~bench fairyring"] = Bench(cam, "fairyring mid");
                wait = 0;
                phase++;
            }
            while (phase >= 400 && phase < 400 + DenShots.Length)
            {
                string name = DenShots[phase - 400];
                if (wait == 0)
                {
                    if (!AimSite(cam, phase - 400, ObjectPart.WolfDen, Structures.WolfU, DenShots, "wolf den", new Vector3(0.9f, 0.45f, 6.5f), -1.2f)) { GameStatus.Lines["~" + name] = "no wolf den placed"; phase++; continue; }
                    wait = 180;
                    return;
                }
                if (--wait > 0 || Loading()) return;
                Shot(cam, null, "warmup");
                Shot(cam, dir, name);
                wait = 0;
                phase++;
            }
            while (phase >= 760 && phase < 760 + OverlookShots.Length)
            {
                string name = OverlookShots[phase - 760];
                if (wait == 0)
                {
                    if (!AimSite(cam, phase - 760, ObjectPart.Overlook, Structures.OverlookU, OverlookShots, "overlook", new Vector3(-1f, 0.15f, 3.5f), 12f)) { GameStatus.Lines["~" + name] = "no overlook placed"; phase++; continue; }
                    wait = 180;
                    return;
                }
                if (--wait > 0 || Loading()) return;
                Shot(cam, null, "warmup");
                Shot(cam, dir, name);
                wait = 0;
                phase++;
            }
            if (phase >= 320 && phase < 320 + VentShots.Length)
            {
                string name = VentShots[phase - 320];
                if (wait == 0)
                {
                    if (!AimVent(cam, phase - 320)) { GameStatus.Lines["~" + name] = "no vent placed"; phase++; }
                    else { wait = 180; return; }
                }
                else
                {
                    if (--wait > 0 || Loading()) return;
                    Shot(cam, null, "warmup");
                    Shot(cam, dir, name);
                    if (phase == 320)
                    {
                        GameStatus.Lines["~bench geyser"] = Bench(cam, "geyser erupting");
                        GameStatus.Lines["~bench geyser column"] = BenchMist(cam, "geyser erupting");
                    }
                    TerrainViewSystem.ClockOverride = null;
                    wait = 0;
                    phase++;
                }
            }
            if (phase >= 330 && phase < 330 + BogShots.Length)
            {
                string name = BogShots[phase - 330];
                if (wait == 0)
                {
                    if (!AimBog(cam, phase - 330)) { GameStatus.Lines["~" + name] = "no bog pool placed"; phase++; }
                    else { wait = 180; return; }
                }
                else
                {
                    if (--wait > 0 || Loading()) return;
                    Shot(cam, null, "warmup");
                    Shot(cam, dir, name);
                    wait = 0;
                    phase++;
                }
            }
            if (phase >= 340 && phase < 340 + MesaShots.Length)
            {
                string name = MesaShots[phase - 340];
                if (wait == 0)
                {
                    if (!AimMesa(cam, phase - 340)) { GameStatus.Lines["~" + name] = "no pose in STABBY_MESA_AT"; phase++; }
                    else { wait = 180; return; }
                }
                else
                {
                    if (--wait > 0 || Loading()) return;
                    Shot(cam, null, "warmup");
                    Shot(cam, dir, name);
                    wait = 0;
                    phase++;
                }
            }
            if ((only == "arch" && phase > 1) || (only == "spring" && phase >= 270 + SpringShots.Length) || (only == "vents" && phase >= 320 + VentShots.Length) || (only == "bog" && phase >= 330 + BogShots.Length) || (only == "mesas" && phase >= 340 + MesaShots.Length) || (only == "inn" && phase >= 160 + InnShots.Length) || (only == "discovery" && phase >= 180 + RingShots.Length) || (only == "camp" && phase >= 190 + CampShots.Length) || (only == "hermit" && phase >= 230 + HermitShots.Length) || (only == "shrine" && phase >= 240 + ShrineShots.Length) || (only == "ruin" && phase >= 260 + RuinShots.Length) || (only == "fairyring" && phase >= 300 + FairyShots.Length) || (only == "wolfden" && phase >= 400 + DenShots.Length) || (only == "overlook" && phase >= 760 + OverlookShots.Length))
            {
                File.WriteAllText(Path.Combine(dir, "stats.txt"), Status());
                Finish(0);
                return;
            }
            while (phase >= 200 && phase < 200 + CreatureShots.Length)
            {
                string name = CreatureShots[phase - 200];
                bool pair = name == "creature-host" || name == "creature-client";
                if (wait == 0)
                {
                    if (CreatureBuildSystem.Count == 0 && --chaseWait > -1800) return;
                    bool stride = name.EndsWith("stride"), run = name.EndsWith("run");
                    FreezeCreatures(true);
                    CreatureView.Current = new CreatureView { Override = 1, Move = stride ? 1 : 0, Phase = stride ? 0.3f : run ? 0.15f : 0, Run = run ? 1 : 0, Alert = name.EndsWith("alert") ? 1 : 0, Show = name == "creature-client" ? 1 : 0 };
                    if (name != "creature-client" && !AimCreature(cam, CreatureRange(name))) { GameStatus.Lines["~" + name] = "no creature spawned"; phase++; continue; }
                    wait = pair ? 240 : 120;
                    return;
                }
                if (name != "creature-client") AimCreature(cam, CreatureRange(name));
                if (pair && CreatureCompare(false) > 0.02f && wait > -1800) { wait--; return; }
                if (--wait > 0 || Loading()) return;
                Shot(cam, null, "warmup");
                Shot(cam, dir, name);
                if (name == "creature-client") CreatureCompare(true);
                wait = 0;
                phase++;
            }
            if (only == "creatures" && phase >= 200 + CreatureShots.Length)
            {
                if (job == null) { CreatureView.Current = default; FreezeCreatures(false); job = ApproachTour(cam, dir); }
                if (job.MoveNext()) return;
                GameStatus.Lines["~creatures"] = CreatureBuildSystem.Count.ToString();
                GameStatus.Lines["~creature plan"] = $"{CreaturePlanSystem.Planned} planned, spawn reach {CreaturePlanSystem.SpawnReach:F2} m, despawn reach {CreaturePlanSystem.DespawnReach:F2} m";
                File.WriteAllText(Path.Combine(dir, "stats.txt"), Status());
                Finish(0);
                return;
            }
            if (phase == 550) { if (job == null) job = WaterLookTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 520) { if (job == null) job = NightTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 560) { if (job == null) job = LightReceiversTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 610) { if (job == null) job = WaterLightsTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 600) { if (job == null) job = OceanDetailTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 580) { if (job == null) job = CavesTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 570) { if (job == null) job = FirelightTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 590) { if (job == null) job = LightScaleTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 540) { if (job == null) job = LightBudgetTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 490) { if (job == null) job = FarHillsTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 510) { if (job == null) job = GeyserPoolTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 480) { if (job == null) job = SprayShadowsTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 470) { if (job == null) job = ShadowTiersTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 430) { if (job == null) job = DesireShots(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 420) { if (job == null) job = BrainOnly(dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 350) { if (job == null) job = ControlsTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 500) { if (job == null) job = DetailTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 450) { if (job == null) job = WaterfallTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 460) { if (job == null) job = SitingTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 440) { if (job == null) job = RiverMouthTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 390) { if (job == null) job = WaterTintTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 380) { if (job == null) job = LiquidsTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 370) { if (job == null) job = RockBedsTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 360) { if (job == null) job = TerrainFixTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 310) { if (job == null) job = SpeciesShot(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 280) { if (job == null) job = FarTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 250) { if (job == null) job = CreatureBench(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 620) { if (job == null) job = CreatureMemory(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 640) { if (job == null) job = CreatureSkin(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 650) { if (job == null) job = CaveMouthTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 660) { if (job == null) job = CaveLightsTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 670) { if (job == null) job = IceCaveTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 690) { if (job == null) job = DungeonsTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 680) { if (job == null) job = DungeonTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 700) { if (job == null) job = MinesTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 720) { if (job == null) job = TownsTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 730) { if (job == null) job = RoadsTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 820) { if (job == null) job = BonesTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 780) { if (job == null) job = BoulderTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 750) { if (job == null) job = CaveFallsTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 770) { if (job == null) job = WallHeadsTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 900) { if (job == null) job = CreaturesYoung(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 740) { if (job == null) job = LavaCaveTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 710) { if (job == null) job = DungeonLinksTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 800) { if (job == null) job = SaltTowerTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 980) { if (job == null) job = CoastFallTour(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 630) { if (job == null) job = RigDump(cam, dir); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 50)
            {
                if (job == null) job = CliffTour(cam, dir);
                if (job.MoveNext()) return;
                File.WriteAllText(Path.Combine(dir, "stats.txt"), Status());
                Finish(0);
                return;
            }
            if (phase == 290) { if (job == null) job = FastWalkTour(cam); if (job.MoveNext()) return; File.WriteAllText(Path.Combine(dir, "stats.txt"), Status()); Finish(0); return; }
            if (phase == 220)
            {
                if (job == null) job = LandformTour(cam, dir);
                if (job.MoveNext()) return;
                File.WriteAllText(Path.Combine(dir, "stats.txt"), Status());
                Finish(0);
                return;
            }
            if (phase == 120)
            {
                if (job == null) job = ScaleTour(cam, dir);
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
                if (--wait > 0 || Loading()) { if (wait == 0) wait = -1; return; }
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
                if (--wait > 0 || Loading()) return;
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
                    if (--wait > 0 || Loading()) return;
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
                    if (--wait > 0 || Loading()) return;
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
                if (--wait > 0 || Loading()) return;
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
                if (--wait > 0 || Loading()) return;
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
                if (--wait > 0 || Loading()) return;
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
                int step = phase == 24 ? 0 : 1 + structStep;
                string name = StructureShots[step];
                if (wait == 0)
                {
                    if (!AimStructure(cam, step)) { GameStatus.Lines["~" + name] = "no structure placed"; phase++; continue; }
                    wait = 180;
                    return;
                }
                if (--wait > 0 || Loading()) return;
                Shot(cam, null, "warmup");
                Shot(cam, dir, name);
                if (step == 1) GameStatus.Lines["~bench structure"] = Bench(cam, "structure mid");
                if (step == 3) GameStatus.Lines["~bench structure far"] = Bench(cam, "structure far");
                wait = 0;
                if (phase == 25 && ++structStep < StructureShots.Length - 1) continue;
                phase++;
            }
            if (only == "structure")
            {
                File.WriteAllText(Path.Combine(dir, "stats.txt"), Status());
                Finish(0);
                return;
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
                if (--wait > 0 || Loading()) return;
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
                if (--wait > 0 || Loading()) return;
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
                if (--wait > 0 || Loading()) return;
                Shot(cam, null, "warmup");
                Shot(cam, dir, name);
                wait = 0;
                phase++;
            }
            while (phase >= 80 && phase < 80 + RiverShots.Length)
            {
                string name = RiverShots[phase - 80];
                if (wait == 0)
                {
                    if (!AimRiver(cam, phase - 80)) { GameStatus.Lines["~" + name] = "no " + name + " found"; phase++; continue; }
                    wait = 180;
                    return;
                }
                if (--wait > 0 || Loading()) return;
                Shot(cam, null, "warmup");
                Shot(cam, dir, name);
                wait = 0;
                phase++;
            }
            if (only == "river")
            {
                if (job == null) job = RiverTour(cam, dir);
                if (job.MoveNext()) return;
            }
            while (phase >= 140 && phase < 140 + WaterShots.Length)
            {
                string name = WaterShots[phase - 140];
                if (wait == 0)
                {
                    if (!AimWater(cam, phase - 140)) { GameStatus.Lines["~" + name] = "no " + name + " found"; phase++; continue; }
                    wait = 180;
                    return;
                }
                if (--wait > 0 || Loading()) return;
                Shot(cam, null, "warmup");
                Shot(cam, dir, name);
                if (phase == 140) GameStatus.Lines["~bench water coast"] = Bench(cam, "water coast low");
                if (phase == 145) GameStatus.Lines["~bench water far"] = Bench(cam, "water far sea");
                wait = 0;
                phase++;
            }
            if (only == "biomes" || only == "beach" || only == "forest" || only == "river" || only == "water")
            {
                File.WriteAllText(Path.Combine(dir, "stats.txt"), Status());
                Finish(0);
                return;
            }
            int f0 = 26 + BiomeShots.Length;
            while ((phase >= f0 && phase < f0 + FloorShots.Length) || (phase >= 100 && phase < 100 + ContactShots.Length))
            {
                string name = phase < 100 ? FloorShots[phase - f0] : ContactShots[phase - 100];
                if (wait == 0)
                {
                    if (!(phase < 100 ? AimFloor(cam, phase - f0) : AimContact(cam, phase - 100))) { GameStatus.Lines["~" + name] = "no " + name + " found"; phase = phase + 1 == f0 + FloorShots.Length ? 100 : phase + 1; continue; }
                    wait = 180;
                    return;
                }
                if (--wait > 0 || Loading()) return;
                Shot(cam, null, "warmup");
                Shot(cam, dir, name);
                var cu = Shader.GetGlobalVector("_StabbyContactUV");
                if (cu.x > 0 && name.StartsWith("contact")) GameStatus.Lines["~" + name + " contact"] = $"{name}: contact window centre {(0.5f - cu.y) / cu.x:F0},{(0.5f - cu.z) / cu.x:F0}, camera {cam.transform.position.x:F0},{cam.transform.position.z:F0}";
                wait = 0;
                phase = phase + 1 == f0 + FloorShots.Length ? 100 : phase + 1;
            }
            if (only == "floor")
            {
                File.WriteAllText(Path.Combine(dir, "stats.txt"), Status());
                Finish(0);
                return;
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
            if (SystemCost.On) GameStatus.Lines["~syscost"] = SystemCost.Report();
            return string.Join("\n", GameStatus.Lines.OrderBy(kv => kv.Key).Select(kv => kv.Value)) +
            $"\ncapsules {GameStatus.Capsules}, terrain {(GameStatus.TerrainBuilt ? "built" : "missing")}\n";
        }

        static void Finish(int code)
        {
            EditorApplication.update -= Poll;
            SpikeProbe.Stop();
            SystemCost.Stop();
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

        static bool AimLake(Camera cam, bool close = false)
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
                float back = close ? 7f : 25f;
                var eye = new Vector3(shore.x - dir.x * back, 0, shore.y - dir.y * back);
                eye.y = Mathf.Max(GroundHeight(eye.x, eye.z), pick.Level) + (close ? 1.8f : 6f);
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

        static readonly string[] CreatureShots = { "creature-close-idle", "creature-close-stride", "creature-mid-idle", "creature-mid-stride", "creature-far-idle", "creature-far-stride", "creature-close-run", "creature-close-alert", "creature-host", "creature-client" };

        static float CreatureRange(string name) => name.Contains("close") ? 4f : name.Contains("far") ? 40f : 15f;

        static bool AimCreature(Camera cam, float range)
        {
            foreach (var w in Unity.Entities.World.All)
            {
                if (w.Name != GameSettings.ViewClient) continue;
                using var q = w.EntityManager.CreateEntityQuery(typeof(CreatureRoot));
                using var roots = q.ToComponentDataArray<CreatureRoot>(Unity.Collections.Allocator.Temp);
                if (roots.Length == 0) return false;
                var c = roots[0];
                int best = int.MaxValue;
                foreach (var r in roots)
                {
                    if (!w.EntityManager.HasComponent<CreatureState>(r.Ghost)) continue;
                    int id = w.EntityManager.GetComponentData<CreatureState>(r.Ghost).Id;
                    if (id < best) { best = id; c = r; }
                }
                var target = new Vector3(c.Position.x, GroundHeight(c.Position.x, c.Position.z) + 0.75f, c.Position.z);
                Vector3 eye = target;
                for (int k = 0; k < 16; k++)
                {
                    float a = c.Heading + (k % 2 == 0 ? 1 : -1) * 0.4f * ((k + 1) / 2);
                    var fwd = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                    var side = new Vector3(fwd.z, 0, -fwd.x);
                    eye = target + side * range + fwd * (range * 0.1f);
                    eye.y = Mathf.Max(GroundHeight(eye.x, eye.z) + 0.6f, target.y + 0.1f + range * 0.08f);
                    if (Clear(eye, target)) break;
                }
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                return true;
            }
            return false;
        }

        static float CreatureCompare(bool log)
        {
            var sets = new System.Collections.Generic.List<(string Name, System.Collections.Generic.Dictionary<int, Unity.Mathematics.float3> States)>();
            foreach (var w in Unity.Entities.World.All)
            {
                bool server = w.IsServer();
                if (!server && System.Array.IndexOf(GameSettings.Clients, w.Name) < 0) continue;
                using var q = w.EntityManager.CreateEntityQuery(typeof(CreatureState));
                using var states = q.ToComponentDataArray<CreatureState>(Unity.Collections.Allocator.Temp);
                var d = new System.Collections.Generic.Dictionary<int, Unity.Mathematics.float3>();
                foreach (var s in states) d[s.Id] = new Unity.Mathematics.float3(s.Position, s.Heading);
                sets.Add((server ? "server" : w.Name, d));
            }
            sets.Sort((a, b) => a.Name == "server" ? -1 : b.Name == "server" ? 1 : string.CompareOrdinal(a.Name, b.Name));
            if (sets.Count < 2) return float.MaxValue;
            float max = 0, maxHeading = 0;
            int shared = 0, missing = 0;
            var host = sets[0].States;
            for (int k = 1; k < sets.Count; k++)
                foreach (var kv in sets[k].States)
                {
                    if (!host.TryGetValue(kv.Key, out var h)) { missing++; continue; }
                    shared++;
                    max = Mathf.Max(max, Unity.Mathematics.math.distance(h.xy, kv.Value.xy));
                    maxHeading = Mathf.Max(maxHeading, Mathf.Abs(h.z - kv.Value.z));
                }
            if (log)
            {
                GameStatus.Lines["~creature worlds"] = string.Join(", ", sets.Select(x => $"{x.Name} {x.States.Count}"));
                GameStatus.Lines["~creature agreement"] = $"{shared} client copies match a server creature, {missing} without one; max position delta {max:F4} m, max heading delta {maxHeading:F5} rad";
                if (host.Count > 0)
                {
                    int first = host.Keys.Min();
                    GameStatus.Lines["~creature first"] = string.Join("; ", sets.Select(x => x.States.TryGetValue(first, out var v) ? $"{x.Name} id {first} at {v.x:F3},{v.y:F3} heading {v.z:F4}" : $"{x.Name} id {first} absent"));
                }
            }
            return missing > 0 || shared == 0 ? float.MaxValue : max;
        }

        static Unity.Entities.World ServerWorld()
        {
            foreach (var w in Unity.Entities.World.All) if (w.IsServer()) return w;
            return null;
        }

        static Unity.Entities.World ViewWorld()
        {
            foreach (var w in Unity.Entities.World.All) if (w.Name == GameSettings.ViewClient) return w;
            return null;
        }

        static void FreezeCreatures(bool on)
        {
            var w = ServerWorld();
            if (w == null) return;
            using var q = w.EntityManager.CreateEntityQuery(typeof(CreatureFreeze));
            if (on && q.IsEmpty) w.EntityManager.CreateEntity(typeof(CreatureFreeze));
            else if (!on && !q.IsEmpty) w.EntityManager.DestroyEntity(q);
        }

        static bool HerdMindOf(Unity.Entities.World w, int herd, out HerdMind mind)
        {
            mind = default;
            var handle = w.GetExistingSystem<CreatureHostSystem>();
            if (handle == SystemHandle.Null) return false;
            w.EntityManager.CompleteAllTrackedJobs();
            ref var sys = ref w.Unmanaged.GetUnsafeSystemRef<CreatureHostSystem>(handle);
            if (!sys.Herds.IsCreated || !sys.Herds.TryGetValue(herd, out var h)) return false;
            mind = h.Mind;
            return true;
        }

        static Vector2 HerdCentre(Unity.Entities.World w, int herd, out int count, out float move)
        {
            using var q = w.EntityManager.CreateEntityQuery(typeof(CreatureState));
            using var states = q.ToComponentDataArray<CreatureState>(Unity.Collections.Allocator.Temp);
            Vector2 sum = default;
            count = 0;
            move = 0;
            foreach (var s in states)
                if (CreaturePlan.HerdOf(s.Id) == herd) { sum += new Vector2(s.Position.x, s.Position.y); move += s.Move; count++; }
            if (count > 0) { sum /= count; move /= count; }
            return sum;
        }

        static void PlaceServerPlayers(Unity.Entities.World w, Vector2 at)
        {
            using var q = w.EntityManager.CreateEntityQuery(typeof(Player), typeof(LocalTransform));
            using var es = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            foreach (var e in es)
            {
                var t = w.EntityManager.GetComponentData<LocalTransform>(e);
                t.Position = new Unity.Mathematics.float3(at.x, GroundHeight(at.x, at.y), at.y);
                w.EntityManager.SetComponentData(e, t);
            }
        }

        const float HerdEyeM = 14f, HerdFov = 40f;

        static void AimHerd(Camera cam, Vector2 centre, Vector2 side)
        {
            var target = new Vector3(centre.x, GroundHeight(centre.x, centre.y) + 0.9f, centre.y);
            var eye = target + new Vector3(side.x, 0, side.y) * HerdEyeM;
            eye.y = Mathf.Max(GroundHeight(eye.x, eye.z) + 1.7f, target.y + 2.5f);
            cam.fieldOfView = HerdFov;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
        }

        static System.Collections.IEnumerator ApproachTour(Camera cam, string dir)
        {
            var server = ServerWorld();
            var view = ViewWorld();
            int herd = int.MaxValue;
            using (var q = server.EntityManager.CreateEntityQuery(typeof(CreatureState)))
            using (var states = q.ToComponentDataArray<CreatureState>(Unity.Collections.Allocator.Temp))
                foreach (var s in states) herd = Mathf.Min(herd, CreaturePlan.HerdOf(s.Id));
            if (herd == int.MaxValue || view == null) { GameStatus.Lines["~approach"] = "approach: no herd on the server"; yield break; }
            Vector2 start = HerdCentre(server, herd, out int members, out _);
            Vector2 d = new Vector2(0, 1), side = new Vector2(1, 0);
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4;
                d = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
                side = new Vector2(d.y, -d.x);
                var t = new Vector3(start.x, GroundHeight(start.x, start.y) + 0.9f, start.y);
                var e = t + new Vector3(side.x, 0, side.y) * HerdEyeM;
                e.y = Mathf.Max(GroundHeight(e.x, e.z) + 1.7f, t.y + 2.5f);
                if (Clear(e, t)) break;
            }
            const float startM = 90f, stopM = 12f, walk = 4f;
            float along = startM, clock = 0, modeClock = 0, lag = 0, peak = 0;
            var log = new System.Collections.Generic.List<string>();
            var lags = new System.Collections.Generic.List<float>();
            float sinceShot = 10f, lagSettled = 0;
            HerdMode last = HerdMode.Calm;
            bool calmShot = false, alertShot = false, fleeShot = false, fled = false;
            Vector2 aim = HerdCentre(view, herd, out int seen0, out _);
            if (seen0 == 0) aim = start;
            while (clock < 120f)
            {
                float dt = Mathf.Min(Time.deltaTime, 0.1f);
                bool walking = clock >= 4f && calmShot;
                if (walking) along = Mathf.Max(stopM, along - walk * dt);
                Vector2 player = start + d * along;
                PlaceServerPlayers(server, player);
                Vector2 viewCentre = HerdCentre(view, herd, out int seen, out _);
                if (seen > 0) aim = Vector2.Lerp(aim, viewCentre, Mathf.Min(1, 3 * dt));
                AimHerd(cam, aim, side);
                Vector2 serverCentre = HerdCentre(server, herd, out _, out float move);
                if (!HerdMindOf(server, herd, out var mind)) { log.Add($"herd {herd} gone at {clock:F1} s"); break; }
                float threat = Vector2.Distance(player, serverCentre);
                if (mind.Mode != last)
                {
                    log.Add($"{clock:F1} s {last}->{mind.Mode} with the player {threat:F1} m from the herd centre");
                    last = mind.Mode;
                    modeClock = 0;
                    if (mind.Mode == HerdMode.Flee) fled = true;
                }
                if (mind.Mode == HerdMode.Flee)
                {
                    peak = Mathf.Max(peak, move);
                    float c = CreatureCompare(false);
                    if (c < float.MaxValue)
                    {
                        lag = Mathf.Max(lag, c);
                        lags.Add(c);
                        if (sinceShot > 0.5f) lagSettled = Mathf.Max(lagSettled, c);
                    }
                }
                string name = !calmShot && clock >= 4f ? "creature-approach-calm"
                    : walking && !alertShot && !fled && mind.Mode == HerdMode.Alert && modeClock > 1.5f ? "creature-approach-alert"
                    : !fleeShot && mind.Mode == HerdMode.Flee && modeClock > 1.2f ? "creature-approach-flee" : null;
                if (name != null)
                {
                    if (!calmShot && mind.Mode != HerdMode.Calm) log.Add($"herd not calm at the calm shot ({mind.Mode})");
                    calmShot = true;
                    if (name.EndsWith("alert")) alertShot = true;
                    if (name.EndsWith("flee")) fleeShot = true;
                    Shot(cam, null, "warmup");
                    Shot(cam, dir, name);
                    sinceShot = 0;
                }
                if (fled && mind.Mode == HerdMode.Calm && modeClock > 2f) break;
                clock += dt;
                modeClock += dt;
                sinceShot += dt;
                yield return null;
            }
            for (float rest = 0; rest < 15f && CreatureCompare(false) > 0.02f; rest += Time.deltaTime)
            {
                PlaceServerPlayers(server, start + d * along);
                aim = Vector2.Lerp(aim, HerdCentre(view, herd, out _, out _), 0.05f);
                AimHerd(cam, aim, side);
                yield return null;
            }
            Vector2 end = HerdCentre(server, herd, out _, out _);
            Shot(cam, null, "warmup");
            Shot(cam, dir, "creature-approach-settled");
            CreatureCompare(true);
            cam.fieldOfView = 60f;
            GameStatus.Lines["~approach"] = $"approach: herd {herd} of {members} at {start.x:F0},{start.y:F0}, player walked {startM:F0}->{stopM:F0} m at {walk} m/s along {d.x:F2},{d.y:F2}; " + string.Join("; ", log);
            GameStatus.Lines["~approach result"] = $"approach result: herd moved {Vector2.Distance(start, end):F1} m and settled {Vector2.Distance(start + d * along, end):F1} m from the player; peak mean move {peak:F2} x walk; max server-client position delta while fleeing {lag:F3} m";
            lags.Sort();
            if (lags.Count > 0) GameStatus.Lines["~approach lag"] = $"approach flee lag over {lags.Count} frames: median {lags[lags.Count / 2]:F3} m, p95 {lags[(int)(lags.Count * 0.95f)]:F3} m, max {lag:F3} m, max outside 0.5 s after a shot {lagSettled:F3} m";
            if (!alertShot || !fleeShot) GameStatus.Lines["~approach missing"] = $"approach missing shots: alert {alertShot}, flee {fleeShot}";
        }

        static bool Clear(Vector3 eye, Vector3 target)
        {
            for (int i = 1; i < 24; i++)
            {
                var p = Vector3.Lerp(eye, target, i / 24f);
                if (GroundHeight(p.x, p.z) > p.y - 0.25f) return false;
            }
            return true;
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

        static System.Collections.IEnumerator LandformTour(Camera cam, string dir)
        {
            Ground ground = default;
            bool found = false;
            foreach (var w in Unity.Entities.World.All)
            {
                var q = w.EntityManager.CreateEntityQuery(typeof(Ground));
                if (q.CalculateEntityCount() != 1) continue;
                ground = q.GetSingleton<Ground>();
                found = true;
                break;
            }
            if (!found) { GameStatus.Lines["~landforms"] = "landforms: no ground"; yield break; }
            foreach (var (name, eye, target) in LandformShots(ground))
            {
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                for (int f = 0; f < 180 || Loading(); f++) yield return null;
                Shot(cam, null, "warmup");
                Shot(cam, dir, name);
                GameStatus.Lines["~" + name] = $"{name} from {eye.x:F0},{eye.y:F0},{eye.z:F0} at {target.x:F0},{target.y:F0},{target.z:F0}";
            }
        }

        static System.Collections.Generic.List<(string name, Vector3 eye, Vector3 target)> LandformShots(Ground ground)
        {
            var shots = new System.Collections.Generic.List<(string name, Vector3 eye, Vector3 target)>();
            for (int s = 0; s < 2; s++)
            {
                int shape = s == 0 ? FeatureEdit.Tor : FeatureEdit.Hoodoo, count = 0, best = -1, bestNear = 0;
                ref var g = ref ground.Blob.Value;
                for (int k = 0; k < g.Edits.Length; k++)
                {
                    if (g.Edits[k].Shape != shape) continue;
                    count++;
                    int near = 0;
                    for (int j = 0; j < g.Edits.Length; j++)
                        if (g.Edits[j].Shape == shape && Mathf.Abs(g.Edits[j].Ax - g.Edits[k].Ax) < 60 && Mathf.Abs(g.Edits[j].Az - g.Edits[k].Az) < 60) near++;
                    if (near > bestNear) { bestNear = near; best = k; }
                }
                string kind = s == 0 ? "tor" : "hoodoo";
                GameStatus.Lines["~landforms " + kind] = $"{count} {kind} edits, densest group {bestNear} at edit {best}";
                if (best < 0) continue;
                Vector3 c = Vector3.zero, top = Vector3.zero;
                int n = 0;
                for (int j = 0; j < g.Edits.Length; j++)
                {
                    var e = g.Edits[j];
                    if (e.Shape != shape || Mathf.Abs(e.Ax - g.Edits[best].Ax) >= 60 || Mathf.Abs(e.Az - g.Edits[best].Az) >= 60) continue;
                    c += new Vector3((e.Ax + e.Bx) * 0.5f, (e.Ay + e.By) * 0.5f, (e.Az + e.Bz) * 0.5f);
                    if (e.MaxY > top.y) top = new Vector3(e.Ax, e.MaxY, e.Az);
                    n++;
                }
                c /= n;
                var dirOut = Vector3.zero;
                float low = float.MaxValue;
                for (int a = 0; a < 8; a++)
                {
                    var d = Quaternion.Euler(0, a * 45f, 0) * Vector3.forward;
                    float h = ground.HeightAt(c.x + d.x * 70, c.z + d.z * 70);
                    if (h < low) { low = h; dirOut = d; }
                }
                var target = new Vector3(c.x, (c.y + top.y) * 0.5f, c.z);
                float back = s == 0 ? 55f : 80f;
                var eye = c + dirOut * back;
                eye.y = Clear(ground, target, eye, Mathf.Max(ground.HeightAt(eye.x, eye.z) + 3f, target.y - 4f), 0.3f);
                shots.Add((s == 0 ? "landform-tor" : "landform-hoodoos", eye, target));
                var far = Vector3.zero;
                far.y = float.MaxValue;
                for (int a = 0; a < 8; a++)
                {
                    var p = c + Quaternion.Euler(0, a * 45f, 0) * Vector3.forward * 420f;
                    p.y = Clear(ground, target, p, Mathf.Max(ground.HeightAt(p.x, p.z) + 40f, target.y + 30f), 0.05f);
                    if (p.y < far.y) far = p;
                }
                shots.Add((s == 0 ? "landform-tor-far" : "landform-hoodoos-far", far, target));
            }
            return shots;
        }

        static float Clear(Ground ground, Vector3 target, Vector3 eye, float y, float from)
        {
            for (float t = from; t <= 1f; t += 0.05f)
            {
                float h = ground.HeightAt(target.x + (eye.x - target.x) * t, target.z + (eye.z - target.z) * t) + 5f;
                y = Mathf.Max(y, target.y + (h - target.y) / t);
            }
            return y;
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
        static readonly string[] FloorShots = { "floor-near", "floor-mid", "cliff-foot", "floor-windthrow", "floor-snapped" };
        static readonly string[] ContactShots = { "contact-gate", "contact-log", "contact-tree", "contact-boulder" };
        static readonly string[] FoliageShots = { "foliage-close", "tree-close", "foliage-mid", "foliage-far", "spruce-close", "oak-close" };

        static System.Collections.IEnumerator ScaleTour(Camera cam, string dir)
        {
            var pose = System.Environment.GetEnvironmentVariable("STABBY_SCALE_POSE")?.Split(',').Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            if (pose != null && pose.Length == 7) cam.transform.SetPositionAndRotation(new Vector3(pose[0], pose[1], pose[2]), new Quaternion(pose[3], pose[4], pose[5], pose[6]));
            else if (!AimFoliage(cam, 1)) { GameStatus.Lines["~scale"] = "scale: no forest found"; yield break; }
            var at = cam.transform;
            GameStatus.Lines["~scale pose"] = string.Format(System.Globalization.CultureInfo.InvariantCulture, "scale pose {0:F2},{1:F2},{2:F2},{3:F5},{4:F5},{5:F5},{6:F5}", at.position.x, at.position.y, at.position.z, at.rotation.x, at.rotation.y, at.rotation.z, at.rotation.w);
            var settle = System.Diagnostics.Stopwatch.StartNew();
            int frames = 0;
            for (; frames < 180 || Loading(); frames++) yield return null;
            GameStatus.Lines["~scale settle"] = $"scale settle: {frames} frames, {settle.Elapsed.TotalSeconds:F1} s, objects pending {ObjectViewSystem.Pending}, terrain behind {GameStatus.TerrainBehind}";
            while (!ObjectScale.Sample("still forest")) yield return null;
            GameStatus.Lines["~scale lod"] = ObjectScale.Forced();
            GameStatus.Lines["~scale memory"] = ObjectScale.Memory();
            GameStatus.Lines["~objects draw"] = DrawStats();
            Shot(cam, null, "warmup");
            Shot(cam, dir, "scale-forest");
            GameStatus.Lines["~bench scale forest"] = Bench(cam, "forest with grass, shadows, terrain and water");
            GameStatus.Lines["~bench scale forest shadows"] = ShadowBench(cam, "forest", true);
            GameStatus.Lines["~bench trees"] = FoliageBench(cam, "trees filling the view");
            var p = at.position;
            var fwd = Vector3.ProjectOnPlane(at.forward, Vector3.up).normalized;
            float rise = p.y - GroundHeight(p.x, p.z);
            do
            {
                p += fwd * 2f;
                p.y = GroundHeight(p.x, p.z) + rise;
                at.position = p;
                yield return null;
            }
            while (!ObjectScale.Sample("walking 2 m per frame"));
            GameStatus.Lines["~scale"] = ObjectScale.Report();
            ObjectScale.Stop();
        }

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

        static readonly string[] InnShots = { "inn-far", "inn-mid", "inn-door", "inn-interior", "inn-site", "inn-roof-close", "inn-roof-side" };

        static bool AimInn(Camera cam, int mode)
        {
            foreach (var w in Unity.Entities.World.All)
            {
                var field = w.Name == GameSettings.ViewClient ? w.GetExistingSystemManaged<ObjectColumnsSystem>()?.Field : null;
                if (field == null) continue;
                int d = field.Library.Objects.FindIndex(o => o.Id == "inn");
                if (d < 0) return false;
                ObjectInstance pick = default;
                float best = float.MaxValue;
                for (int cz = 0; cz < field.ChunksAcross; cz++)
                    for (int cx = 0; cx < field.ChunksAcross; cx++)
                        foreach (var o in field.Chunk(cx, cz))
                        {
                            if (o.Def != d) continue;
                            float score = new Vector2(o.X - home.x, o.Z - home.z).magnitude;
                            if (score < best) { best = score; pick = o; }
                        }
                if (best == float.MaxValue) return false;
                var part = field.Library.Objects[d].Parts[0];
                var l = Structures.Layout(ObjectPart.Inn, ObjectMeshes.Key("inn", 0, pick.Variant));
                var q = new Quaternion(pick.Qx, pick.Qy, pick.Qz, pick.Qw);
                var pos = new Vector3(pick.X, pick.Y, pick.Z);
                float s = pick.Size, k = s * part.ScaleX * Structures.InnU, floor = Structures.Fy * k;
                var across = q * Vector3.forward;
                var along = q * Vector3.right;
                var up = Vector3.up;
                var centre = pos + q * new Vector3(0, 0, part.OffsetZ * s);
                var door = pos + q * new Vector3(part.ScaleX * l.DoorX * s, 0, (part.OffsetZ + part.ScaleZ * l.DoorZ) * s);
                Vector3 eye, target;
                if (mode == 0) { target = centre + up * 4f * k; eye = target + (across * 0.85f + along * 0.5f).normalized * 110f + up * 35f; }
                else if (mode == 1) { target = centre + up * 3f * k; eye = target + (across * 0.85f + along * 0.45f).normalized * 22f * k + up * 5f * k; }
                else if (mode == 2) { target = door + up * (floor + 1.2f * k); eye = target + across * 4.5f * k + along * 1.4f * k + up * 0.4f * k; }
                else if (mode == 3) { eye = door - across * 1.3f * k + up * (floor + 1.65f * k); target = centre - across * 0.4f * part.ScaleZ * l.DoorZ * s + along * (centre - door).magnitude * 0.2f + up * (floor + 0.9f * k); }
                else if (mode == 6) { float side = l.DoorX > 0 ? -1 : 1; target = centre + across * (part.ScaleZ * l.DoorZ * s + 0.5f * k) + up * (floor + 3f * k); eye = target + along * side * 5f * k + across * 1.8f * k + up * 0.6f * k; }
                else if (mode == 5) { float side = l.DoorX > 0 ? -1 : 1; target = centre + across * (part.ScaleZ * l.DoorZ * s + 0.6f * k) + along * side * 2.2f * k + up * (floor + 2.3f * k); eye = target + across * 2.6f * k + along * side * 1.3f * k + up * 0.9f * k; }
                else { target = pos + up * 1.5f; eye = target + across * 0.8f * s + along * 0.35f * s + up * 0.85f * s; }
                if (mode != 3)
                {
                    var ground = w.EntityManager.CreateEntityQuery(typeof(Ground)).GetSingleton<Ground>();
                    eye.y = Mathf.Max(eye.y, ground.HeightAt(eye.x, eye.z) + 1.4f);
                }
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                GameStatus.Lines["~aim " + InnShots[mode]] = $"inn variant {pick.Variant} size {pick.Size:F1} m at {pick.X:F0},{pick.Y:F0},{pick.Z:F0}, door {door.x:F1},{door.z:F1}, eye {eye.x:F1},{eye.y:F1},{eye.z:F1}";
                return true;
            }
            return false;
        }

        static readonly string[] RingShots = { "ring-close", "ring-mid" };

        static bool AimRing(Camera cam, int mode)
        {
            foreach (var w in Unity.Entities.World.All)
            {
                var field = w.Name == GameSettings.ViewClient ? w.GetExistingSystemManaged<ObjectColumnsSystem>()?.Field : null;
                if (field == null) continue;
                ObjectInstance pick = default;
                float best = float.MaxValue;
                foreach (var o in field.Discoveries())
                {
                    if (field.Library.Objects[o.Def].Parts[0].Shape != ObjectPart.StoneRing) continue;
                    float score = new Vector2(o.X - home.x, o.Z - home.z).magnitude;
                    if (score < best) { best = score; pick = o; }
                }
                if (best == float.MaxValue) return false;
                var q = new Quaternion(pick.Qx, pick.Qy, pick.Qz, pick.Qw);
                var centre = new Vector3(pick.X, pick.Y, pick.Z);
                float r = Structures.RingRadius * pick.Size;
                var across = q * Vector3.forward;
                var along = q * Vector3.right;
                Vector3 eye, target;
                if (mode == 0) { target = centre + along * 0.6f * r + Vector3.up * 1.2f; eye = centre - across * 0.35f * r - along * 0.5f * r + Vector3.up * 1.7f; }
                else { target = centre + Vector3.up * 1.0f; eye = centre + (across * 0.8f + along * 0.6f).normalized * 4.2f * r + Vector3.up * 0.9f * r; }
                var ground = w.EntityManager.CreateEntityQuery(typeof(Ground)).GetSingleton<Ground>();
                eye.y = Mathf.Max(eye.y, ground.HeightAt(eye.x, eye.z) + 1.6f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                GameStatus.Lines["~aim " + RingShots[mode]] = $"stone ring variant {pick.Variant} size {pick.Size:F1} m at {pick.X:F0},{pick.Y:F0},{pick.Z:F0}, eye {eye.x:F1},{eye.y:F1},{eye.z:F1}";
                return true;
            }
            return false;
        }

        static readonly string[] CampShots = { "camp-close", "camp-mid" };
        static readonly string[] HermitShots = { "hermit-close", "hermit-mid" };
        static readonly string[] ShrineShots = { "shrine-close", "shrine-mid" };
        static readonly string[] RuinShots = { "ruin-close", "ruin-mid" };
        static readonly string[] SpringShots = { "spring-close", "spring" };
        static readonly string[] FairyShots = { "fairyring-close", "fairyring-mid" };
        static readonly string[] DenShots = { "wolfden-close", "wolfden-mid" };
        static readonly string[] OverlookShots = { "overlook-view", "overlook-site" };
        static readonly string[] VentShots = { "geyser", "geyser-far", "mudpot", "geyser-cone", "mudpot-rim" };

        static bool AimVent(Camera cam, int mode)
        {
            int kind = mode == 2 || mode == 4 ? ObjectField.MudPot : ObjectField.Geyser;
            foreach (var w in Unity.Entities.World.All)
            {
                var field = w.Name == GameSettings.ViewClient ? w.GetExistingSystemManaged<ObjectColumnsSystem>()?.Field : null;
                if (field == null) continue;
                ObjectInstance pick = default;
                float best = float.MaxValue;
                foreach (var o in field.Discoveries())
                {
                    if (!field.IsVent(o) || field.VentKind(o) != kind) continue;
                    float score = new Vector2(o.X - home.x, o.Z - home.z).magnitude;
                    if (score < best) { best = score; pick = o; }
                }
                if (best == float.MaxValue) return false;
                var pools = new System.Collections.Generic.List<float>();
                field.SpringPoolsAt(pick, pools);
                var c = new Vector3(pools[0], pools[4], pools[1]);
                float r = pools[2];
                Vector3 eye, target;
                var side = new Vector3(0.8f, 0, 0.6f);
                if (mode == 3)
                {
                    Structures.GeyserCycle(field.VentSeed(pick), out float interval, out float duration, out float offset);
                    TerrainViewSystem.ClockOverride = offset + duration + 0.5 * (interval - duration);
                    float rout = pools[3];
                    target = c + Vector3.up * 0.1f * rout;
                    eye = c + side * 1.5f * rout + Vector3.up * 0.7f * rout;
                }
                else if (mode == 4)
                {
                    TerrainViewSystem.ClockOverride = null;
                    target = c + side * 0.6f * r;
                    eye = c + side * 1.9f * r + Vector3.up * 0.9f;
                }
                else if (kind == ObjectField.Geyser)
                {
                    Structures.GeyserCycle(field.VentSeed(pick), out float interval, out float duration, out float offset);
                    TerrainViewSystem.ClockOverride = offset + 0.4 * duration;
                    float h = Stabby.Viewer.WaterGeometry.ColumnPerRadius * r;
                    target = c + Vector3.up * 0.4f * h;
                    eye = c + side * (mode == 0 ? 1.4f : 4.5f) * h + Vector3.up * (mode == 0 ? 2f : 0.6f * h);
                }
                else
                {
                    TerrainViewSystem.ClockOverride = null;
                    target = c;
                    eye = c + side * 2.6f * r + Vector3.up * 1.7f;
                }
                var ground = w.EntityManager.CreateEntityQuery(typeof(Ground)).GetSingleton<Ground>();
                eye.y = Mathf.Max(eye.y, ground.HeightAt(eye.x, eye.z) + 1.6f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                GameStatus.Lines["~aim " + VentShots[mode]] = $"vent kind {kind} pool r {r:F2} m at {c.x:F0},{c.y:F0},{c.z:F0}, eye {eye.x:F1},{eye.y:F1},{eye.z:F1}, clock {TerrainViewSystem.ClockOverride:F1}";
                return true;
            }
            return false;
        }

        static readonly string[] BogShots = { "bog", "bog-wide" };

        static bool AimBog(Camera cam, int mode)
        {
            foreach (var w in Unity.Entities.World.All)
            {
                var field = w.Name == GameSettings.ViewClient ? w.GetExistingSystemManaged<ObjectColumnsSystem>()?.Field : null;
                if (field == null) continue;
                var bogs = field.BogPools();
                int pick = -1;
                float best = float.MaxValue;
                for (int k = 0; k < bogs.Length; k += ObjectField.BogStride)
                {
                    float score = new Vector2(bogs[k] - home.x, bogs[k + 1] - home.z).magnitude - 40f * bogs[k + 2];
                    if (score < best) { best = score; pick = k; }
                }
                if (pick < 0) return false;
                var c = new Vector3(bogs[pick], bogs[pick + 3], bogs[pick + 1]);
                float r = bogs[pick + 2];
                var side = new Vector3(0.8f, 0, 0.6f);
                var eye = c + side * (mode == 0 ? 1.6f : 5f) * r + Vector3.up * (mode == 0 ? 1.7f : 0.9f * r);
                var ground = w.EntityManager.CreateEntityQuery(typeof(Ground)).GetSingleton<Ground>();
                eye.y = Mathf.Max(eye.y, ground.HeightAt(eye.x, eye.z) + 1.6f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(c - eye));
                GameStatus.Lines["~aim " + BogShots[mode]] = $"bog pool r {r:F2} m depth {bogs[pick + 3] - bogs[pick + 4]:F2} m at {c.x:F0},{c.y:F1},{c.z:F0}, {bogs.Length / ObjectField.BogStride} pools";
                return true;
            }
            return false;
        }

        static readonly string[] MesaShots = { "mesa", "butte" };
        const string MesaVariable = "STABBY_MESA_AT";

        static bool AimMesa(Camera cam, int mode)
        {
            var spots = System.Environment.GetEnvironmentVariable(MesaVariable)?.Split(';');
            if (spots == null || spots.Length <= mode) return false;
            var v = spots[mode].Split(',').Select(t => float.Parse(t, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            foreach (var w in Unity.Entities.World.All)
            {
                var q = w.EntityManager.CreateEntityQuery(typeof(Ground));
                if (q.CalculateEntityCount() != 1) continue;
                var ground = q.GetSingleton<Ground>();
                Vector3 eye, target;
                if (v.Length >= 6)
                {
                    eye = new Vector3(v[0], v[1], v[2]);
                    target = new Vector3(v[3], v[4], v[5]);
                }
                else
                {
                    float r = v[2], back = (mode == 0 ? 3f : 4.5f) * r, low = float.MaxValue;
                    var dirOut = Vector3.forward;
                    for (int a = 0; a < 8; a++)
                    {
                        var d = Quaternion.Euler(0, a * 45f, 0) * Vector3.forward;
                        float h = ground.HeightAt(v[0] + d.x * back, v[1] + d.z * back);
                        if (h < low) { low = h; dirOut = d; }
                    }
                    target = new Vector3(v[0], 0.5f * (ground.HeightAt(v[0], v[1]) + low), v[1]);
                    eye = target + dirOut * back;
                    eye.y = Clear(ground, target, eye, Mathf.Max(low + 20f, target.y), 0.3f);
                }
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                GameStatus.Lines["~aim " + MesaShots[mode]] = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F0},{1:F0},{2:F0},{3:F0},{4:F0},{5:F0} top {6:F0}", eye.x, eye.y, eye.z, target.x, target.y, target.z, ground.HeightAt(target.x, target.z));
                return true;
            }
            return false;
        }

        static bool AimCamp(Camera cam, int mode) => AimSite(cam, mode, ObjectPart.Campsite, Structures.CampU, CampShots, "campsite", new Vector3(0.7f, -0.7f, 4.5f), 0);

        static bool AimSite(Camera cam, int mode, int shape, float unit, string[] shots, string label, Vector3 close, float focusZ)
        {
            foreach (var w in Unity.Entities.World.All)
            {
                var field = w.Name == GameSettings.ViewClient ? w.GetExistingSystemManaged<ObjectColumnsSystem>()?.Field : null;
                if (field == null) continue;
                ObjectInstance pick = default;
                float best = float.MaxValue;
                foreach (var o in field.Discoveries())
                {
                    if (field.Library.Objects[o.Def].Parts[0].Shape != shape || (variantOnly == "niche" && !Structures.WaysideNiche(ObjectMeshes.Key(field.Library.Objects[o.Def].Id, 0, o.Variant))) || (variantOnly != null && variantOnly != "niche" && o.Variant.ToString() != variantOnly)) continue;
                    float score = new Vector2(o.X - home.x, o.Z - home.z).magnitude;
                    if (score < best) { best = score; pick = o; }
                }
                if (best == float.MaxValue) return false;
                var q = new Quaternion(pick.Qx, pick.Qy, pick.Qz, pick.Qw);
                var centre = new Vector3(pick.X, pick.Y, pick.Z);
                float u = unit * pick.Size;
                var across = q * Vector3.forward;
                var along = q * Vector3.right;
                Vector3 eye, target;
                if (shape == ObjectPart.Overlook && mode == 0) { eye = centre - across * 3.2f + along * 0.6f + Vector3.up * 3.4f; target = centre + across * 30f - Vector3.up * 9f; }
                else if (shape == ObjectPart.Overlook) { eye = centre + along * 32f - across * 6f + Vector3.up * 10f; target = centre + across * 3f; }
                else if (mode == 0) { target = centre + across * focusZ * u + Vector3.up * 0.5f * u; eye = centre + (across * close.x + along * close.y) * close.z * u + Vector3.up * 1.7f; }
                else { target = centre + Vector3.up * 0.6f * u; eye = centre + (across * 0.8f + along * 0.6f).normalized * 16f * u + Vector3.up * 6f * u; }
                var ground = w.EntityManager.CreateEntityQuery(typeof(Ground)).GetSingleton<Ground>();
                eye.y = Mathf.Max(eye.y, ground.HeightAt(eye.x, eye.z) + 1.6f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                GameStatus.Lines["~aim " + shots[mode]] = $"{label} variant {pick.Variant}{(shape == ObjectPart.Wayside && Structures.WaysideNiche(ObjectMeshes.Key(field.Library.Objects[pick.Def].Id, 0, pick.Variant)) ? " niche" : "")} size {pick.Size:F1} m at {pick.X:F0},{pick.Y:F0},{pick.Z:F0}, eye {eye.x:F1},{eye.y:F1},{eye.z:F1}";
                return true;
            }
            return false;
        }

        static readonly string[] StructureShots = { "structure-close", "structure-mid", "structure-lod1", "structure-far" };
        static readonly float[] StructureBack = { 0.75f, 7.5f, 17f, 30f };
        static int structStep;

        static bool AimStructure(Camera cam, int step)
        {
            bool close = step == 0;
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
                            int s0 = field.Library.Objects[o.Def].Parts[0].Shape;
                            if (s0 != ObjectPart.Wall && s0 != ObjectPart.Cairn) continue;
                            float score = new Vector2(o.X - home.x, o.Z - home.z).magnitude;
                            if (field.Library.Objects[o.Def].Parts[0].Shape == ObjectPart.Wall)
                            {
                                int run = 0;
                                foreach (var b in chunk)
                                    if (b.Def == o.Def && (b.X - o.X) * (b.X - o.X) + (b.Z - o.Z) * (b.Z - o.Z) < 36f * o.Size * o.Size) run++;
                                score -= 1e6f + (run >= 4 ? 400f + 40f * run : 0f);
                            }
                            if (score < best) { best = score; pick = o; }
                        }
                    }
                if (best == float.MaxValue) return false;
                var target = new Vector3(pick.X, pick.Y + pick.Size * 0.1f, pick.Z);
                var across = new Vector3(2 * (pick.Qx * pick.Qz + pick.Qw * pick.Qy), 0, 1 - 2 * (pick.Qx * pick.Qx + pick.Qy * pick.Qy)).normalized;
                var along = new Vector3(across.z, 0, -across.x);
                float back = StructureBack[step] * pick.Size;
                var eye = target + (across * 0.85f + along * 0.5f) * back + Vector3.up * (close ? 0.25f * pick.Size : (step == 1 ? 0.3f : 0.4f) * back);
                var ground = w.EntityManager.CreateEntityQuery(typeof(Ground)).GetSingleton<Ground>();
                eye.y = Mathf.Max(eye.y, ground.HeightAt(eye.x, eye.z) + 1.4f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                GameStatus.Lines["~aim " + StructureShots[step]] = $"{field.Library.Objects[pick.Def].Id} size {pick.Size:F1} m at {pick.X:F0},{pick.Y:F0},{pick.Z:F0}, eye {back:F0} m back";
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
                string want = mode < 2 ? "fallen-log" : mode == 2 ? "cliff-scree" : mode == 3 ? "windthrow" : "snapped-tree";
                for (int cz = 0; cz < field.ChunksAcross; cz++)
                    for (int cx = 0; cx < field.ChunksAcross; cx++)
                    {
                        var chunk = field.Chunk(cx, cz);
                        foreach (var o in chunk)
                        {
                            string id = lib[o.Def].Id;
                            if (id != want) continue;
                            int near = 0;
                            foreach (var b in chunk)
                            {
                                string bid = lib[b.Def].Id;
                                float d2 = (b.X - o.X) * (b.X - o.X) + (b.Z - o.Z) * (b.Z - o.Z);
                                if (d2 < 400f && (mode == 2 ? bid == "cliff-scree" : bid == "fallen-log" || bid == "stump" || bid == "windthrow" || bid == "snapped-tree")) near++;
                            }
                            float score = new Vector2(o.X - home.x, o.Z - home.z).magnitude * 0.02f - near;
                            if (score < best) { best = score; pick = o; }
                        }
                    }
                if (best == float.MaxValue) return false;
                var ground = w.EntityManager.CreateEntityQuery(typeof(Ground)).GetSingleton<Ground>();
                var trees = new System.Collections.Generic.List<Vector2>();
                float reach = mode == 0 || mode > 2 ? 12f : 30f;
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
                float back = mode == 0 ? 4f : mode == 1 ? 16f : mode == 2 ? 26f : 7f, rise = mode == 0 ? 1.4f : mode == 1 ? 5f : mode == 2 ? 4f : 2.2f;
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

        static readonly string[] WaterShots = { "coast-low", "beach-close", "beach-mid", "lake", "lake-close", "sea-far", "sea-low" };

        static bool AimWater(Camera cam, int view)
        {
            switch (view)
            {
                case 0: return Coast(cam, 40f);
                case 1: return AimBeach(cam, 0, false);
                case 2: return AimBeach(cam, 0, true);
                case 3: return AimLake(cam, false);
                case 4: return AimLake(cam, true);
                case 5: return FarCoast(cam, 1000f, 250f);
                default: return FarCoast(cam, 260f, 9f);
            }
        }

        static readonly string[] ForestShots = { "temperate-forest", "boreal", "temperate-rain" };
        static readonly string[] RiverShots = { "river-close", "river-bend", "river-steep", "river-steep-mid" };

        static bool AimRiver(Camera cam, int view)
        {
            foreach (var w in Unity.Entities.World.All)
            {
                var map = w.GetExistingSystemManaged<ClientMapSystem>()?.Map;
                if (map == null) continue;
                float cm = map.Config.CellMeters, best = float.MinValue, turnAt = 0;
                RiverSegment pick = null;
                int pk = 0;
                foreach (var r in map.Water.Rivers)
                    for (int k = 8; k + 8 < r.X.Count; k += 2)
                    {
                        if (r.Width[k] < 5f) continue;
                        var a = new Vector2(r.X[k - 8], r.Y[k - 8]) * cm;
                        var p = new Vector2(r.X[k], r.Y[k]) * cm;
                        var z = new Vector2(r.X[k + 8], r.Y[k + 8]) * cm;
                        var din = (p - a).normalized;
                        var dout = (z - p).normalized;
                        float slope = Mathf.Max(0, r.Surface[k - 8] - r.Surface[k + 8]) / Mathf.Max((z - a).magnitude, 1f);
                        float turn = din.x * dout.y - din.y * dout.x;
                        float s = view == 0 ? 1 - 20 * slope - Mathf.Abs(turn) : view == 1 ? Mathf.Abs(turn) - 10 * slope : slope;
                        s /= 1 + (p - new Vector2(home.x, home.z)).magnitude / 1500f;
                        if (s <= best) continue;
                        best = s;
                        pick = r;
                        pk = k;
                        turnAt = turn;
                    }
                if (pick == null) return false;
                float wy = Hydrology.Stage(map.Config, pick, pk), half = Mathf.Max(pick.Width[pk] * 0.5f, 6f);
                var c = new Vector3(pick.X[pk] * cm, wy, pick.Y[pk] * cm);
                var along = new Vector3(pick.X[pk + 4] - pick.X[pk - 4], 0, pick.Y[pk + 4] - pick.Y[pk - 4]).normalized;
                var left = new Vector3(-along.z, 0, along.x);
                var inner = turnAt >= 0 ? left : -left;
                Vector3 eye, target;
                float lift;
                if (view == 0) { eye = c + left * (half + 5f) - along * 4f; target = c + along * 14f - left * (half * 0.3f); lift = 2.2f; }
                else if (view == 1) { eye = c - inner * (half + 4f) - along * 6f; target = c + inner * (half * 0.55f) + along * 4f; lift = 4f; }
                else if (view == 2) { eye = c - along * 10f + left * (half + 3f); target = c + along * 10f; lift = 3.5f; }
                else { eye = c - along * 45f + left * (half + 30f); target = c + along * 10f; lift = 22f; }
                eye.y = Mathf.Max(GroundHeight(eye.x, eye.z), wy) + lift;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                GameStatus.Lines["~aim " + RiverShots[view]] = $"river {pick.Id} at {c.x:F0},{c.y:F0},{c.z:F0}, width {pick.Width[pk]:F0} m, turn {turnAt:F2}, score {best:F3}";
                return true;
            }
            return false;
        }

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

        static System.Collections.IEnumerator RiverTour(Camera cam, string dir)
        {
            ClientMapSystem sys = null;
            Ground ground = default;
            foreach (var w in Unity.Entities.World.All)
            {
                var s = w.GetExistingSystemManaged<ClientMapSystem>();
                var q = w.EntityManager.CreateEntityQuery(typeof(Ground));
                if (s?.Map == null || q.CalculateEntityCount() != 1) continue;
                sys = s;
                ground = q.GetSingleton<Ground>();
                break;
            }
            if (sys == null) { GameStatus.Lines["~rivers"] = "no map"; yield break; }
            var map = sys.Map;
            float cm = map.Config.CellMeters;
            string[] kinds = { "bend", "steep" };
            var spots = new Vector3[2];
            var alongs = new Vector3[2];
            var turns = new float[2];
            var widths = new float[2];
            var dist = new float[] { float.MaxValue, float.MaxValue };
            foreach (var r in map.Water.Rivers)
                for (int k = 3; k + 3 < r.X.Count; k++)
                {
                    float ax = r.X[k] - r.X[k - 3], az = r.Y[k] - r.Y[k - 3], bx = r.X[k + 3] - r.X[k], bz = r.Y[k + 3] - r.Y[k];
                    float la = Mathf.Sqrt(ax * ax + az * az), lb = Mathf.Sqrt(bx * bx + bz * bz);
                    if (la < 1e-3f || lb < 1e-3f) continue;
                    float turn = (ax * bz - az * bx) / (la * lb), hw = 0.5f * r.Width[k];
                    float bend = Mathf.Abs(turn) * hw / (0.5f * (la + lb) * cm);
                    float slope = (r.Surface[k - 3] - r.Surface[k + 3]) / ((la + lb) * cm);
                    var p = new Vector3(r.X[k] * cm, r.Surface[k], r.Y[k] * cm);
                    float d = (new Vector2(p.x, p.z) - new Vector2(home.x, home.z)).sqrMagnitude;
                    int pick = r.Width[k] >= 12f && bend >= 0.12f ? 0 : r.Width[k] >= 5f && slope >= 0.05f && r.Surface[k] >= 25f ? 1 : -1;
                    if (pick < 0 || d >= dist[pick]) continue;
                    dist[pick] = d; spots[pick] = p; turns[pick] = turn; widths[pick] = r.Width[k];
                    alongs[pick] = new Vector3(bx + ax, 0, bz + az).normalized;
                }
            for (int k = 0; k < 2; k++)
            {
                if (dist[k] == float.MaxValue) { GameStatus.Lines["~river " + kinds[k]] = "no " + kinds[k] + " found"; continue; }
                var t = spots[k];
                var al = alongs[k];
                var left = new Vector3(-al.z, 0, al.x);
                float hw = 0.5f * widths[k];
                var inner = turns[k] > 0 ? left : -left;
                GameStatus.Lines["~river " + kinds[k]] = $"{kinds[k]} at {t.x:F0},{t.y:F0},{t.z:F0}, width {widths[k]:F0} m, turn {turns[k]:F2}, {Mathf.Sqrt(dist[k]):F0} m from the player";
                var eyes = new Vector3[]
                {
                    k == 0 ? t + inner * (hw + 14f) + Vector3.up * 5f : t + al * (hw + 35f) + Vector3.up * 7f,
                    t - al * (hw * 2.5f + 25f) + inner * hw * 0.5f + Vector3.up * (hw * 0.4f + 8f),
                    t - al * 160f + inner * 120f + Vector3.up * 260f,
                };
                var looks = new Vector3[] { k == 0 ? t - inner * hw : t, t - inner * hw * 0.3f, t };
                string[] names = { "close", "along", "aerial" };
                for (int v = 0; v < 3; v++)
                {
                    var eye = eyes[v];
                    eye.y = Mathf.Max(eye.y, ground.HeightAt(eye.x, eye.z) + 1.8f);
                    var look = looks[v];
                    look.y = Mathf.Max(ground.HeightAt(look.x, look.z), t.y);
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
                    for (var w = Settle(180); w.MoveNext();) yield return null;
                    Shot(cam, null, "warmup");
                    Shot(cam, dir, "river-" + kinds[k] + "-" + names[v]);
                }
            }
            for (var d = BankDetails(cam, dir, ground); d.MoveNext();) yield return null;
            for (var f = FallsTour(cam, dir, map, ground); f.MoveNext();) yield return null;
        }

        static readonly string[] BankShots = { "reeds", "river-gravel", "driftwood", "wet-stone" };

        static System.Collections.IEnumerator BankDetails(Camera cam, string dir, Ground ground)
        {
            ObjectField field = null;
            float span = 0;
            foreach (var w in Unity.Entities.World.All)
                if (w.Name == GameSettings.ViewClient)
                {
                    field = w.GetExistingSystemManaged<ObjectColumnsSystem>()?.Field;
                    var map = w.GetExistingSystemManaged<ClientMapSystem>()?.Map;
                    if (map != null) span = WorldChunks.Size * map.Config.CellMeters;
                }
            if (field == null || span <= 0) { GameStatus.Lines["~bank details"] = "no object field"; yield break; }
            int across = field.ChunksAcross, hx = (int)(home.x / span), hz = (int)(home.z / span);
            foreach (var id in BankShots)
            {
                int def = field.Library.Objects.FindIndex(o => o.Id == id);
                var all = new System.Collections.Generic.List<ObjectInstance>();
                for (int cz = Mathf.Max(0, hz - 2); cz <= Mathf.Min(across - 1, hz + 2); cz++)
                    for (int cx = Mathf.Max(0, hx - 2); cx <= Mathf.Min(across - 1, hx + 2); cx++)
                        foreach (var o in field.Chunk(cx, cz)) if (o.Def == def) all.Add(o);
                if (all.Count == 0) { GameStatus.Lines["~bank " + id] = "no " + id + " near the player"; continue; }
                ObjectInstance pick = all[0];
                float best = float.MinValue;
                for (int k = 0; k < all.Count; k += Mathf.Max(1, all.Count / 800))
                {
                    var a = all[k];
                    int count = 0;
                    for (int j = 0; j < all.Count; j += Mathf.Max(1, all.Count / 4000))
                        if ((all[j].X - a.X) * (all[j].X - a.X) + (all[j].Z - a.Z) * (all[j].Z - a.Z) < 64f) count++;
                    float sc = count - (new Vector2(a.X - home.x, a.Z - home.z)).magnitude / 300f;
                    if (sc > best) { best = sc; pick = a; }
                }
                float back = id == "driftwood" ? 5f + pick.Size : id == "reeds" ? 6.5f : 1.8f;
                Vector3 eye = Vector3.zero, shore = Vector3.zero;
                float low = float.MaxValue, high = float.MinValue;
                for (int a = 0; a < 8; a++)
                {
                    var d = Quaternion.Euler(0, a * 45f, 0) * Vector3.forward;
                    var e = new Vector3(pick.X, 0, pick.Z) + d * back;
                    float h = ground.HeightAt(e.x, e.z);
                    if (h < low) { low = h; eye = e; }
                    if (h > high && h < pick.Y + 3f) { high = h; shore = e; }
                }
                bool landward = (id == "reeds" || id == "driftwood" || id == "river-gravel") && high > float.MinValue;
                if (landward) { eye = shore; low = high; }
                eye.y = Mathf.Max(low, pick.Y) + (id == "reeds" ? 2.4f : id == "driftwood" ? 1.6f : id == "wet-stone" ? 6f : 2.8f);
                var look = new Vector3(pick.X, pick.Y + (id == "reeds" ? 0.6f * pick.Size : 0.2f), pick.Z);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
                GameStatus.Lines["~bank " + id] = $"{id} at {pick.X:F0},{pick.Y:F1},{pick.Z:F0}, {all.Count} near, cluster {best:F1}";
                for (int k = 0; k < 180 || Loading(); k++) yield return null;
                Shot(cam, null, "warmup");
                Shot(cam, dir, "bank-" + id);
            }
        }

        const float BasinPitch = 35f;

        static System.Collections.IEnumerator FallsTour(Camera cam, string dir, GeneratedWorld map, Ground ground, int views = 4)
        {
            var best = default(Stabby.Viewer.FallCurtain);
            bool found = false;
            int falls = 0, verts = 0, tris = 0, hugged = 0, buried = 0, buriedFree = 0, tucked = 0, nearFoot = 0;
            float depth = 0, depthMax = 0, depthFree = 0;
            var detail = new TerrainDetail(map);
            var sw = new System.Diagnostics.Stopwatch();
            var near = new Vector2(-1, -1);
            var at = System.Environment.GetEnvironmentVariable("STABBY_FALL_NEAR")?.Split(',');
            if (at != null && at.Length == 2) near = new Vector2(float.Parse(at[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(at[1], System.Globalization.CultureInfo.InvariantCulture));
            foreach (var l in NearField.Lines(map))
                for (int k = 1; k < l.X.Length; k++)
                    if (Stabby.Viewer.WaterGeometry.FallAt(l, k, out var f, out _))
                    {
                        falls++;
                        foreach (var part in Stabby.Viewer.WaterGeometry.Split(f))
                        {
                            float fh = part.Drop + 0.5f, fhalf = 0.5f * part.Width;
                            int fr = Stabby.Viewer.WaterGeometry.CurtainRowsOf(fh), fc = Stabby.Viewer.WaterGeometry.CurtainColsOf(part.RunWidth);
                            verts += fr * fc;
                            tris += 4 * (fr - 1) * (fc - 1);
                            sw.Start();
                            var off = Stabby.Viewer.WaterGeometry.CurtainOffsets(part, detail);
                            sw.Stop();
                            for (int j = 1; j < fr; j++)
                            {
                                float u = j / (fr - 1f), y = u * u * fh, vy = part.LipY - y, free = Stabby.Viewer.WaterGeometry.FallThrowAt(y);
                                for (int c = 0; c < fc; c++)
                                {
                                    float uc = part.SideAt(c, fc), sc = uc * fhalf, ba = part.Bow * uc * uc, o = off[j * fc + c], x = part.LipX + part.DirZ * sc + part.DirX * ba, z = part.LipZ - part.DirX * sc + part.DirZ * ba;
                                    if (o > free + 0.01f) hugged++;
                                    if (vy < part.BaseY) continue;
                                    float g = detail.Sample(x + part.DirX * o, z + part.DirZ * o), gf = detail.Sample(x + part.DirX * free, z + part.DirZ * free);
                                    if (g > vy && g > part.LipY + 0.3f) tucked++;
                                    else if (g > vy) { buried++; depth += g - vy; depthMax = Mathf.Max(depthMax, g - vy); if (vy < part.BaseY + 1) nearFoot++; }
                                    if (gf > vy && gf <= part.LipY + 0.3f) { buriedFree++; depthFree += gf - vy; }
                                }
                            }
                        }
                        bool better = near.x >= 0
                            ? (new Vector2(f.LipX, f.LipZ) - near).sqrMagnitude < (new Vector2(best.LipX, best.LipZ) - near).sqrMagnitude
                            : f.Bow > 0 != best.Bow > 0 ? f.Bow > 0 : f.Drop > best.Drop;
                        if (!found || better) { best = f; found = true; }
                    }
            if (!found) { GameStatus.Lines["~falls"] = "no falls found"; yield break; }
            var fd = new Vector3(best.DirX, 0, best.DirZ);
            var side = new Vector3(-fd.z, 0, fd.x);
            var lip = new Vector3(best.LipX, best.LipY, best.LipZ);
            var foot = new Vector3(best.BaseX, best.BaseY, best.BaseZ);
            float h = best.Drop, w = best.Width;
            GameStatus.Lines["~falls"] = $"{(best.Bow > 0 ? "shelf" : "tallest")} fall of {(best.Runs == null ? 1 : best.Runs.Length / 2)} curtains at {lip.x:F0},{lip.y:F0},{lip.z:F0}, drop {h:F1} m, width {w:F0} m, pool {best.PoolRadius:F1} m, discharge {best.Discharge:F0}, rows {Stabby.Viewer.WaterGeometry.CurtainRowsOf(h + 0.5f)} x cols {Stabby.Viewer.WaterGeometry.CurtainColsOf(w)}; {falls} falls, curtain verts {verts}, tris {tris}, hugged {hugged}, above-water verts in face rock {buried} (free arc {buriedFree}), mean depth {depth / Mathf.Max(buried, 1):F2} m (free arc {depthFree / Mathf.Max(buriedFree, 1):F2} m), max {depthMax:F2} m, within 1 m of the pool {nearFoot}, edge verts tucked into banks above the lip {tucked}, offsets {sw.ElapsedMilliseconds} ms";
            var eyes = new[]
            {
                foot + fd * (2.5f * h + 10f) + side * (0.25f * w) + Vector3.up * (0.6f * h + 1.5f),
                foot + fd * (4f * h + 40f) + side * (0.6f * w + 15f) + Vector3.up * (h + 10f),
                foot + fd * (6f * h + 170f) + side * (w + 90f) + Vector3.up * (2f * h + 70f),
                default,
            };
            string[] names = { "falls-close", "falls-mid", "falls-far", "falls-basin" };
            var basin = foot + fd * (0.3f * w);
            float reach = Mathf.Max(1.5f * h, 3f * w) + 20f;
            eyes[3] = basin + (fd * Mathf.Cos(BasinPitch * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(BasinPitch * Mathf.Deg2Rad)) * reach;
            for (int v = 0; v < views; v++)
            {
                var eye = eyes[v];
                var look = v == 3 ? basin : (lip + foot) * 0.5f;
                eye.y = Mathf.Max(eye.y, ground.HeightAt(eye.x, eye.z) + 1.8f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
                for (var s = Settle(180); s.MoveNext();) yield return null;
                Shot(cam, null, "warmup");
                Shot(cam, dir, names[v]);
                if (v == 0) GameStatus.Lines["~bench falls"] = Bench(cam, "falls close");
                if (v == 0) GameStatus.Lines["~bench falls mist"] = BenchMist(cam, "falls close");
            }
        }

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

        static string BenchMist(Camera cam, string label)
        {
            var sb = new System.Text.StringBuilder($"bench {label} mist (ms, off/on):");
            foreach (var (w, h) in new[] { (1920, 1080), (3840, 2160) })
            {
                var ms = new double[2];
                for (int k = 0; k < 4; k++)
                {
                    Shader.SetGlobalFloat("_StabbyMistOff", (k & 1) == 0 ? 1 : 0);
                    ms[k & 1] += BenchAt(cam, w, h) / 2;
                }
                Shader.SetGlobalFloat("_StabbyMistOff", 0);
                sb.Append($" {h}p {ms[0]:F2}/{ms[1]:F2}");
            }
            return sb.ToString();
        }

        static readonly System.Collections.Generic.List<(string, Vector3, Quaternion)> poses = new System.Collections.Generic.List<(string, Vector3, Quaternion)>(), lows = new System.Collections.Generic.List<(string, Vector3, Quaternion)>();
        static System.Collections.IEnumerator job;
        static bool castDone;

        static System.Collections.IEnumerator Settle(int frames)
        {
            for (int k = 0; k < frames || Loading(); k++) yield return null;
        }

        const double LoadTimeoutSeconds = 300;
        const float ReadyWithin = 2f;
        static double loadSince = -1, loadWaited, loadTotal;
        static readonly System.Text.StringBuilder loadWaits = new System.Text.StringBuilder();

        static double LoadWaitTotal() => loadTotal + (loadSince >= 0 ? EditorApplication.timeSinceStartup - loadSince : 0);

        static bool Loading()
        {
            var cam = Camera.main;
            bool columns = true;
            foreach (var w in Unity.Entities.World.All)
                if (w.Name == GameSettings.ViewClient) columns = w.GetExistingSystemManaged<ObjectColumnsSystem>()?.Settled ?? false;
            bool busy = cam == null || GameStatus.TerrainBehind > 0 || !columns || !ObjectViewSystem.ReadyAround(cam.transform.position, ReadyWithin);
            double now = EditorApplication.timeSinceStartup;
            if (!busy)
            {
                if (loadSince >= 0) { loadWaited += now - loadSince; loadTotal += now - loadSince; }
                loadSince = -1;
                return false;
            }
            if (loadSince < 0) loadSince = now;
            if (now - loadSince > LoadTimeoutSeconds && GameStatus.Failure == null)
                GameStatus.Failure = $"capture regions not loaded after {LoadTimeoutSeconds:F0} s at {(cam == null ? "no camera" : cam.transform.position.ToString("F0"))}: terrain behind {GameStatus.TerrainBehind}, object regions {(columns ? "ready" : "building")}, objects pending {ObjectViewSystem.Pending}";
            return true;
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
            RequirePinnedTime("bench");
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
            RequirePinnedTime("capture " + name);
            GameStatus.ToolFrame = Time.frameCount;
            var rt = new RenderTexture(1600, 900, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            if (dir != null)
            {
                File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
                loadWaits.Append(loadWaits.Length == 0 ? "" : ", ").Append(name).Append(' ').Append(loadWaited.ToString("F1"));
                GameStatus.Lines["~load waits"] = $"region load wait before each shot (s): {loadWaits}; {LoadWaitTotal():F0} s waiting, {EditorApplication.timeSinceStartup - SessionState.GetFloat(StartKey, 0) - LoadWaitTotal():F0} s counted toward the {TimeoutSeconds:F0} s timeout";
                loadWaited = 0;
            }
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }
    }
}
