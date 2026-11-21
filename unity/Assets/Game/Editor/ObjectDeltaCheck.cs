using System.Collections.Generic;
using System.IO;
using System.Linq;
using Stabby.World;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;

namespace Stabby.Game.Editor
{
    [InitializeOnLoad]
    public static class ObjectDeltaCheck
    {
        const string Key = "Stabby.ObjectDeltaCheck", StartKey = "Stabby.ObjectDeltaCheck.Start", DirKey = "Stabby.ObjectDeltaCheck.Dir";
        const double TimeoutSeconds = 300;
        static int phase, wait;
        static ulong near, far;
        static ObjectInstance target;
        static readonly List<string> log = new List<string>();

        static ObjectDeltaCheck()
        {
            if (SessionState.GetBool(Key, false)) EditorApplication.update += Poll;
        }

        public static void Run()
        {
            GameCapture.BuildScene();
            using (var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp))
            {
                socket.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
                System.Environment.SetEnvironmentVariable(GameSettings.PortVariable, ((System.Net.IPEndPoint)socket.LocalEndPoint).Port.ToString());
            }
            var args = System.Environment.GetCommandLineArgs();
            int at = System.Array.IndexOf(args, "-stabbyShots");
            SessionState.SetString(DirKey, at >= 0 && at + 1 < args.Length ? args[at + 1] : "");
            SessionState.SetFloat(StartKey, (float)EditorApplication.timeSinceStartup);
            SessionState.SetBool(Key, true);
            EditorApplication.update += Poll;
            EditorApplication.EnterPlaymode();
        }

        static Unity.Entities.World Find(string name)
    {
        var all = Unity.Entities.World.All;
        for (int i = 0; i < all.Count; i++)
            if (name == null ? all[i].IsServer() : all[i].Name == name) return all[i];
        return null;
    }

        static T Sys<T>(string name) where T : SystemBase => Find(name)?.GetExistingSystemManaged<T>();

        static bool Ready(string name)
        {
            var w = Find(name);
            if (w == null || Sys<ObjectColumnsSystem>(name)?.Field == null) return false;
            using var q = w.EntityManager.CreateEntityQuery(typeof(ObjectColumns));
            return q.CalculateEntityCount() == 1;
        }

        static ObjectColumns Columns(string name)
        {
            using var q = Find(name).EntityManager.CreateEntityQuery(typeof(ObjectColumns));
            return q.GetSingleton<ObjectColumns>();
        }

        static float3 PlayerOf(string client)
        {
            var w = Find(client);
            using var q = w.EntityManager.CreateEntityQuery(typeof(Player), typeof(GhostOwnerIsLocal), typeof(LocalTransform));
            return q.CalculateEntityCount() == 0 ? new float3(float.NaN) : q.GetSingleton<LocalTransform>().Position;
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup - SessionState.GetFloat(StartKey, 0) > TimeoutSeconds) { Fail("timed out"); return; }
            if (!EditorApplication.isPlaying) return;
            if (GameStatus.Failure != null) { Fail(GameStatus.Failure); return; }
            if (GameStatus.Synced.Count < GameSettings.Clients.Length || GameStatus.Capsules < 2) return;
            if (!Ready(null) || GameSettings.Clients.Any(c => !Ready(c))) return;
            if (wait > 0) { wait--; return; }
            var server = Sys<ServerObjectDeltaSystem>(null);
            var field = Sys<ObjectColumnsSystem>(null).Field;
            if (phase == 0)
            {
                var p = PlayerOf(GameSettings.ViewClient);
                if (float.IsNaN(p.x)) return;
                if (!Pick(field, p)) { Fail("no interactive objects near the view player or none far from it"); return; }
                log.Add($"near {Describe(field, near)} at ({target.X:F1}, {target.Y:F1}, {target.Z:F1}), player at ({p.x:F1}, {p.y:F1}, {p.z:F1}); far {Describe(field, far)}");
                Aim();
                wait = 240;
                phase = 1;
                return;
            }
            if (phase == 1)
            {
                int region = ObjectIds.Chunk(near);
                if (!Columns(null).Resident(region) || GameSettings.Clients.Any(c => !Columns(c).Resident(region))) { Expect(phase, 1200); return; }
                tries = 0;
                Isolated(field);
                Shot("before");
                Sys<ClientObjectDeltaSystem>(GameSettings.FullStreamClient).Request(far);
                tries = 0;
                phase = 2;
                return;
            }
            if (phase == 2)
            {
                string reply = Sys<ClientObjectDeltaSystem>(GameSettings.FullStreamClient).LastReply;
                if (reply.Length == 0) { Expect(phase, 600); return; }
                Check(reply.EndsWith(ObjectDeltaApply.Words(RemoveResult.OutOfReach)), $"far request over RPC answered '{reply}'");
                Check(!server.Deltas.Removed(far), "far object not recorded");
                server.Submit(near, new float3(target.X, target.Y, target.Z));
                server.Submit(near, new float3(target.X, target.Y, target.Z));
                tries = 0;
                phase = 3;
                return;
            }
            if (phase == 3)
            {
                bool done = server.Deltas.Removed(near) && GameSettings.Clients.All(c => Sys<ClientObjectDeltaSystem>(c).Deltas.Removed(near));
                if (!done) { Expect(phase, 600); return; }
                Check(server.Accepted == 1, $"server accepted {server.Accepted} of two identical submits, rejected {server.Rejected}");
                var cols = new List<ObjectColumn>();
                field.ColumnsOf(target, cols);
                foreach (string w in new[] { null, GameSettings.ViewClient, GameSettings.FullStreamClient })
                {
                    int left = Columns(w).Alive(near);
                    Check(left == 0, $"{w ?? "server"}: {cols.Count} columns of the removed object, {left} left in the static collision");
                }
                var view = Sys<ObjectViewSystem>(GameSettings.ViewClient);
                if (view != null && view.Enabled) Check(view.HiddenCount == 1, $"view client hides {view.HiddenCount} object");
                wait = 120;
                phase = 4;
                return;
            }
            if (phase == 4)
            {
                Shot("after");
                foreach (string c in GameSettings.Clients) log.Add($"{c}: {Sys<ClientObjectDeltaSystem>(c).Deltas.Count} removed, {Sys<ClientObjectDeltaSystem>(c).Killed} columns cleared");
                Finish(failed ? 1 : 0);
            }
        }

        static int tries;
        static bool failed;

        static void Expect(int at, int frames)
        {
            if (++tries > frames) Fail($"phase {at} waited {frames} frames");
        }

        static void Isolated(ObjectField field)
        {
            var cols = new List<ObjectColumn>();
            field.ColumnsOf(target, cols);
            var oc = Columns(GameSettings.FullStreamClient);
            int region = ObjectIds.Chunk(target.Id);
            var data = oc.Regions[region];
            var saved = new List<(int, ObjectColumn)>();
            for (int k = 0; k < data.Owners.Length; k++)
                if (data.Owners[k] == target.Id) saved.Add((k, data.Items[k]));
            int killed = oc.Kill(target.Id);
            Check(killed == cols.Count && cols.Count > 0 && oc.Alive(target.Id) == 0, $"{GameSettings.FullStreamClient}: killed {killed} of {cols.Count} columns in its own copy");
            foreach (string w in new[] { null, GameSettings.ViewClient })
            {
                int left = Columns(w).Alive(target.Id);
                Check(left == cols.Count, $"{w ?? "server"}: {left} of {cols.Count} columns kept after a kill in {GameSettings.FullStreamClient}");
            }
            foreach (var (k, c) in saved) data.Items[k] = c;
            log.Add($"kill isolation: {cols.Count} columns killed in {GameSettings.FullStreamClient} only, then restored");
        }

        static void Check(bool ok, string what)
        {
            log.Add((ok ? "ok   " : "FAIL ") + what);
            if (!ok) failed = true;
        }

        static string Describe(ObjectField field, ulong id) => $"{ObjectDeltaApply.Name(field, id)} id {id:x} (chunk {ObjectIds.Chunk(id)}, ordinal {ObjectIds.Ordinal(id)})";

        static bool Pick(ObjectField field, float3 p)
        {
            float best = float.MaxValue;
            bool found = false, farFound = false;
            var cols = new List<ObjectColumn>();
            int across = field.ChunksAcross;
            float cm = field.ChunkMeters;
            int pcx = (int)(p.x / cm), pcz = (int)(p.z / cm);
            for (int r = 0; r < across && !(found && farFound); r++)
                for (int cz = math.max(0, pcz - r); cz <= math.min(across - 1, pcz + r); cz++)
                    for (int cx = math.max(0, pcx - r); cx <= math.min(across - 1, pcx + r); cx++)
                    {
                        if (math.max(math.abs(cx - pcx), math.abs(cz - pcz)) != r) continue;
                        foreach (var o in field.Chunk(cx, cz))
                        {
                            if (!field.Library.IsInteractive(o.Def)) continue;
                            float d = math.distance(new float3(o.X, o.Y, o.Z), p);
                            if (r > 1) { if (!farFound && d > 200) { far = o.Id; farFound = true; } continue; }
                            cols.Clear();
                            field.ColumnsOf(o, cols);
                            float score = cols.Count > 0 ? d : d + 1e6f;
                            if (score < best) { best = score; near = o.Id; target = o; found = true; }
                            if (!farFound && d > 200) { far = o.Id; farFound = true; }
                        }
                    }
            return found && farFound;
        }

        static void Aim()
        {
            var cam = Camera.main;
            GameStatus.HoldCamera = true;
            var at = new Vector3(target.X, target.Y, target.Z);
            var eye = at + new Vector3(2.2f, 1.6f, 2.2f);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
        }

        static void Shot(string name)
        {
            string dir = SessionState.GetString(DirKey, "");
            if (dir.Length == 0 || Application.isBatchMode && SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            Directory.CreateDirectory(dir);
            var cam = Camera.main;
            GameStatus.ToolFrame = Time.frameCount;
            var rt = new RenderTexture(1280, 720, 24);
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

        static void Fail(string why)
        {
            log.Add("FAIL " + why);
            Finish(1);
        }

        static void Finish(int code)
        {
            EditorApplication.update -= Poll;
            SessionState.EraseBool(Key);
            GameStatus.PutFrames();
            string status = string.Join("\n", GameStatus.Lines.Where(kv => kv.Key.Contains("object")).OrderBy(kv => kv.Key).Select(kv => kv.Value));
            Debug.Log("ObjectDeltaCheck " + (code == 0 ? "passed" : "failed") + "\n" + string.Join("\n", log) + "\n" + status);
            EditorApplication.Exit(code);
        }
    }
}
