using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Stabby.World;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Stabby.Game.Editor
{
    [InitializeOnLoad]
    public static class ContactBurstCheck
    {
        const string Key = "Stabby.ContactBurstCheck", StartKey = "Stabby.ContactBurstCheck.Start";
        const double TimeoutSeconds = 400;
        const int Res = ContactFieldSystem.Res;
        static int wait = 120;
        static bool failed;
        static readonly List<string> log = new List<string>();

        static ContactBurstCheck()
        {
            if (SessionState.GetBool(Key, false)) EditorApplication.update += Poll;
        }

        public static void Run()
        {
            GameCapture.BuildScene();
            GameCapture.PinTime();
            using (var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp))
            {
                socket.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
                Environment.SetEnvironmentVariable(GameSettings.PortVariable, ((System.Net.IPEndPoint)socket.LocalEndPoint).Port.ToString());
            }
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

        static float3 PlayerOf(string client)
        {
            using var q = Find(client).EntityManager.CreateEntityQuery(typeof(Player), typeof(GhostOwnerIsLocal), typeof(LocalTransform));
            return q.CalculateEntityCount() == 0 ? new float3(float.NaN) : q.GetSingleton<LocalTransform>().Position;
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup - SessionState.GetFloat(StartKey, 0) > TimeoutSeconds) { Fail("timed out"); return; }
            if (!EditorApplication.isPlaying) return;
            if (GameStatus.Failure != null) { Fail(GameStatus.Failure); return; }
            if (GameStatus.Synced.Count < GameSettings.Clients.Length || GameStatus.Capsules < 2) return;
            if (Sys<ObjectColumnsSystem>(null)?.Field == null || Sys<ObjectColumnsSystem>(GameSettings.ViewClient)?.Field == null) return;
            var contact = Sys<ContactFieldSystem>(GameSettings.ViewClient);
            if (contact == null || !contact.Built) return;
            if (wait > 0) { wait--; return; }
            var p = PlayerOf(GameSettings.ViewClient);
            if (float.IsNaN(p.x)) return;
            try
            {
                var field = Sys<ObjectColumnsSystem>(null).Field;
                Live(contact, Sys<ObjectColumnsSystem>(GameSettings.ViewClient).Field);
                Splats(field, p);
                Deltas(field, p);
            }
            catch (Exception e) { Fail(e.ToString()); return; }
            Finish(failed ? 1 : 0);
        }

        static void Live(ContactFieldSystem contact, ObjectField field)
        {
            var w = contact.Window;
            var reference = new byte[Res * Res * 4];
            int count = Make(field, new HashSet<ulong>(), reference, w.X0 + 0.5f * Res * w.TexelM, w.Z0 + 0.5f * Res * w.TexelM, w.TexelM, out var again);
            Check(again.X0 == w.X0 && again.Z0 == w.Z0, $"live window rebuilt at ({again.X0}, {again.Z0}) vs ({w.X0}, {w.Z0})");
            int diff = Differ(reference, contact.Data);
            Check(diff == 0 && count == contact.Columns, $"live {GameSettings.ViewClient} contact field: {diff} bytes differ, columns {contact.Columns} vs reference {count}, {contact.Tables} chunk tables");
        }

        static void Splats(ObjectField field, float3 p)
        {
            var data = new NativeArray<byte>(Res * Res * 4, Allocator.Persistent);
            var columns = new NativeReference<int>(Allocator.Persistent);
            var reference = new byte[Res * Res * 4];
            var cases = new (float dx, float dz, float texel, int goneEvery)[] { (0, 0, 0.5f, 0), (0, 0, 0.5f, 5), (37.3f, -61.9f, 0.25f, 0), (-140.6f, 88.1f, 1f, 3), (12.7f, 5.2f, 0.37f, 7) };
            int warm = 0;
            foreach (var (dx, dz, texel, goneEvery) in cases)
            {
                float cx = p.x + dx, cz = p.z + dz;
                var win = ContactWindow.Around(field, Res, cx, cz, texel);
                var chunks = new NativeHashMap<int, ContactChunk>(64, Allocator.Persistent);
                var gone = new NativeHashSet<ulong>(64, Allocator.Persistent);
                var goneManaged = new HashSet<ulong>();
                int n = field.ChunksAcross, tables = 0, k = 0;
                var sw = Stopwatch.StartNew();
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                for (int kz = win.Az; kz <= win.Bz; kz++)
                    for (int kx = win.Ax; kx <= win.Bx; kx++)
                    {
                        chunks[kz * n + kx] = ObjectChunks.Contacts(field, kx, kz);
                        tables++;
                    }
                double convertMs = sw.Elapsed.TotalMilliseconds;
                long convertAlloc = GC.GetAllocatedBytesForCurrentThread() - a0;
                if (goneEvery > 0)
                    for (int kz = win.Az; kz <= win.Bz; kz++)
                        for (int kx = win.Ax; kx <= win.Bx; kx++)
                            foreach (var o in field.Chunk(kx, kz))
                                if (k++ % goneEvery == 0) { gone.Add(o.Id); goneManaged.Add(o.Id); }
                var job = new ContactSplatJob { Chunks = chunks, Gone = gone, Data = data, Columns = columns, Window = win, Across = n };
                if (warm++ == 0) job.Run();
                var splatTimes = new List<double>();
                long splatAlloc = 0;
                for (int r = 0; r < 5; r++)
                {
                    long b0 = GC.GetAllocatedBytesForCurrentThread();
                    sw.Restart();
                    job.Run();
                    splatTimes.Add(sw.Elapsed.TotalMilliseconds);
                    splatAlloc = Math.Max(splatAlloc, GC.GetAllocatedBytesForCurrentThread() - b0);
                }
                var makeTimes = new List<double>();
                long makeAlloc = 0;
                int count = 0;
                for (int r = 0; r < 3; r++)
                {
                    long b0 = GC.GetAllocatedBytesForCurrentThread();
                    sw.Restart();
                    count = Make(field, goneManaged, reference, cx, cz, texel, out _);
                    makeTimes.Add(sw.Elapsed.TotalMilliseconds);
                    makeAlloc = Math.Max(makeAlloc, GC.GetAllocatedBytesForCurrentThread() - b0);
                }
                int diff = Differ(reference, data);
                Check(diff == 0 && count == columns.Value, $"splat around {cx:F1},{cz:F1} at {texel} m, {gone.Count} gone: {diff} bytes differ, columns {columns.Value} vs {count}");
                if (diff > 0)
                {
                    int most = 0;
                    for (int b = 0; b < reference.Length; b++) most = Math.Max(most, Math.Abs(reference[b] - data[b]));
                    var burst = data.ToArray();
                    job.Execute();
                    int managedDiff = Differ(reference, data), crossDiff = Differ(burst, data);
                    log.Add($"  diagnosis: largest byte delta {most}; same job managed vs old Make {managedDiff} bytes differ, managed vs Burst {crossDiff}");
                }
                double make = Median(makeTimes), splat = Median(splatTimes);
                log.Add($"  {tables} chunks: old Make {make:F1} ms ({make / tables:F3} ms/chunk, {makeAlloc / 1024} KB); once-only conversion {convertMs:F1} ms ({convertMs / tables:F3} ms/chunk, {convertAlloc / 1024} KB); Burst splat {splat:F2} ms ({splat / tables:F4} ms/chunk, {splatAlloc} B)");
                foreach (var kv in chunks) kv.Value.Dispose();
                chunks.Dispose();
                gone.Dispose();
            }
            data.Dispose();
            columns.Dispose();
        }

        static void Deltas(ObjectField field, float3 p)
        {
            var tables = new ReachTables();
            tables.Ready(field, new List<int>());
            int n = field.ChunksAcross;
            float cm = field.ChunkMeters;
            int pcx = (int)math.floor(p.x / cm), pcz = (int)math.floor(p.z / cm);
            var sw = Stopwatch.StartNew();
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            int built = 0;
            for (int cz = math.max(0, pcz - 3); cz <= math.min(n - 1, pcz + 3); cz++)
                for (int cx = math.max(0, pcx - 3); cx <= math.min(n - 1, pcx + 3); cx++)
                {
                    tables.Chunks[cz * n + cx] = ObjectChunks.Reach(field, cz * n + cx);
                    built++;
                }
            log.Add($"reach tables: {built} chunks converted once in {sw.Elapsed.TotalMilliseconds:F1} ms ({sw.Elapsed.TotalMilliseconds / built:F3} ms/chunk, {(GC.GetAllocatedBytesForCurrentThread() - a0) / 1024} KB)");

            var interactive = new List<ObjectInstance>();
            ulong still = 0;
            for (int cz = math.max(0, pcz - 1); cz <= math.min(n - 1, pcz + 1); cz++)
                for (int cx = math.max(0, pcx - 1); cx <= math.min(n - 1, pcx + 1); cx++)
                    foreach (var o in field.Chunk(cx, cz))
                        if (field.Library.IsInteractive(o.Def)) interactive.Add(o);
                        else still = o.Id;
            var deltas = new ObjectDeltas();
            for (int k = 0; k < interactive.Count; k += 9)
            {
                deltas.Apply(interactive[k].Id);
                tables.Remove(interactive[k].Id);
            }
            var asks = new List<RemoveAsk>();
            foreach (var o in interactive)
            {
                asks.Add(new RemoveAsk { Id = o.Id, At = new float3(o.X, o.Y, o.Z) });
                asks.Add(new RemoveAsk { Id = o.Id, At = p });
                asks.Add(new RemoveAsk { Id = o.Id, At = new float3(o.X + 1.3f, o.Y + 0.4f, o.Z - 0.9f) });
                asks.Add(new RemoveAsk { Id = o.Id, At = new float3(o.X + 400, o.Y, o.Z) });
            }
            if (interactive.Count > 0)
            {
                var o = interactive[0];
                asks.Add(new RemoveAsk { Id = ObjectIds.Make(n * n, o.Def, 0), At = p });
                asks.Add(new RemoveAsk { Id = ObjectIds.Make(ObjectIds.Chunk(o.Id), 255, 0), At = p });
                asks.Add(new RemoveAsk { Id = ObjectIds.Make(ObjectIds.Chunk(o.Id), o.Def, int.MaxValue), At = p });
                asks.Add(new RemoveAsk { Id = ObjectIds.Make(ObjectIds.Chunk(o.Id), o.Def, ObjectIds.Ordinal(o.Id)) ^ 0x1000000UL, At = p });
            }
            if (still != 0) asks.Add(new RemoveAsk { Id = still, At = p });

            var oldResults = new RemoveResult[asks.Count];
            var oldFound = new bool[asks.Count];
            var oldObjects = new ObjectInstance[asks.Count];
            long b0 = GC.GetAllocatedBytesForCurrentThread();
            sw.Restart();
            for (int k = 0; k < asks.Count; k++) oldResults[k] = deltas.Check(field, asks[k].Id, asks[k].At.x, asks[k].At.y, asks[k].At.z, out _);
            double oldCheckMs = sw.Elapsed.TotalMilliseconds;
            long oldCheckAlloc = GC.GetAllocatedBytesForCurrentThread() - b0;
            for (int k = 0; k < asks.Count; k++) oldFound[k] = ObjectDeltas.TryFind(field, asks[k].Id, out oldObjects[k]);

            var nAsks = new NativeArray<RemoveAsk>(asks.ToArray(), Allocator.Persistent);
            var results = new NativeArray<byte>(asks.Count, Allocator.Persistent);
            var check = new RemoveCheckJob { Chunks = tables.Chunks, Interactive = tables.Interactive, Removed = tables.Removed, Asks = nAsks, Results = results, Across = tables.Across };
            check.Run();
            b0 = GC.GetAllocatedBytesForCurrentThread();
            sw.Restart();
            check.Run();
            double newCheckMs = sw.Elapsed.TotalMilliseconds;
            long newCheckAlloc = GC.GetAllocatedBytesForCurrentThread() - b0;
            int mismatch = 0;
            var tally = new int[6];
            for (int k = 0; k < asks.Count; k++)
            {
                tally[(int)oldResults[k]]++;
                if ((RemoveResult)results[k] != oldResults[k]) mismatch++;
            }
            Check(mismatch == 0, $"removal checks: {mismatch} of {asks.Count} differ (ok {tally[0]}, no chunk {tally[1]}, no object {tally[2]}, not interactive {tally[3]}, removed {tally[4]}, out of reach {tally[5]})");
            log.Add($"  removal check of {asks.Count} asks: managed {oldCheckMs:F2} ms ({oldCheckAlloc} B), Burst {newCheckMs:F3} ms ({newCheckAlloc} B)");

            var ids = new NativeArray<ulong>(asks.Select(a => a.Id).ToArray(), Allocator.Persistent);
            var found = new NativeArray<ObjectInstance>(asks.Count, Allocator.Persistent);
            var ok = new NativeArray<byte>(asks.Count, Allocator.Persistent);
            new ResolveJob { Chunks = tables.Chunks, Ids = ids, Found = found, Ok = ok, Across = tables.Across }.Run();
            int resolveDiff = 0, skipped = 0;
            for (int k = 0; k < asks.Count; k++)
            {
                bool interactiveId = ObjectIds.Def(asks[k].Id) < field.Library.Objects.Count && field.Library.IsInteractive(ObjectIds.Def(asks[k].Id));
                if (!interactiveId) { skipped++; if (ok[k] != 0) resolveDiff++; continue; }
                if ((ok[k] != 0) != oldFound[k] || oldFound[k] && !found[k].Equals(oldObjects[k])) resolveDiff++;
            }
            Check(resolveDiff == 0, $"resolve: {resolveDiff} of {asks.Count} differ ({skipped} non-interactive ids found by neither the Burst job nor replicated)");

            var nearIds = new NativeList<ulong>(16, Allocator.Persistent);
            var nearDist = new NativeList<float>(16, Allocator.Persistent);
            var oldIds = new List<ulong>();
            var rng = Rng.Stream(7, "contact-burst-check");
            int reachDiff = 0, positions = 0, hits = 0;
            double oldReachMs = 0, newReachMs = 0;
            long oldReachAlloc = 0, newReachAlloc = 0;
            var probes = new List<float3>();
            foreach (var o in interactive.Take(150)) probes.Add(new float3(o.X + 0.7f, o.Y + 0.2f, o.Z - 0.4f));
            for (int k = 0; k < 150; k++) probes.Add(new float3(p.x + rng.Range(-cm, cm), p.y, p.z + rng.Range(-cm, cm)));
            foreach (var q in probes)
            {
                var job = InReachJob.Around(field, q);
                job.Chunks = tables.Chunks;
                job.Ids = nearIds;
                job.Dist = nearDist;
                if (positions == 0) job.Run();
                b0 = GC.GetAllocatedBytesForCurrentThread();
                sw.Restart();
                ObjectDeltas.InReach(field, q.x, q.y, q.z, oldIds);
                oldReachMs += sw.Elapsed.TotalMilliseconds;
                oldReachAlloc += GC.GetAllocatedBytesForCurrentThread() - b0;
                b0 = GC.GetAllocatedBytesForCurrentThread();
                sw.Restart();
                job.Run();
                newReachMs += sw.Elapsed.TotalMilliseconds;
                newReachAlloc += GC.GetAllocatedBytesForCurrentThread() - b0;
                positions++;
                hits += oldIds.Count;
                bool same = oldIds.Count == nearIds.Length;
                for (int k = 0; same && k < oldIds.Count; k++) same = oldIds[k] == nearIds[k];
                if (!same) reachDiff++;
            }
            Check(reachDiff == 0, $"in-reach lists: {reachDiff} of {positions} positions differ ({hits} objects in reach in all)");
            log.Add($"  in-reach per pick: managed {oldReachMs / positions:F3} ms ({oldReachAlloc / positions} B), Burst {newReachMs / positions:F4} ms ({newReachAlloc / positions} B)");

            nearIds.Dispose();
            nearDist.Dispose();
            ids.Dispose();
            found.Dispose();
            ok.Dispose();
            nAsks.Dispose();
            results.Dispose();
            tables.Dispose();
        }

        static double Median(List<double> v)
        {
            v.Sort();
            return v[v.Count / 2];
        }

        static int Differ(byte[] a, NativeArray<byte> b)
        {
            int diff = 0;
            for (int k = 0; k < a.Length; k++)
                if (a[k] != b[k]) diff++;
            return diff;
        }

        static readonly List<ObjectInstance> objects = new List<ObjectInstance>();
        static readonly List<ObjectColumn> columnList = new List<ObjectColumn>(), lushList = new List<ObjectColumn>();

        static int Make(ObjectField field, HashSet<ulong> gone, byte[] data, float cx, float cz, float texelM, out ContactWindow win)
        {
            float span = Res * texelM;
            float x0 = Mathf.Floor((cx - 0.5f * span) / texelM) * texelM, z0 = Mathf.Floor((cz - 0.5f * span) / texelM) * texelM;
            win = new ContactWindow { X0 = x0, Z0 = z0, TexelM = texelM };
            Array.Clear(data, 0, data.Length);
            float cm = field.ChunkMeters;
            int n = field.ChunksAcross;
            int ax = Math.Max(0, (int)Math.Floor((x0 - 2f) / cm)), bx = Math.Min(n - 1, (int)Math.Floor((x0 + span + 2f) / cm));
            int az = Math.Max(0, (int)Math.Floor((z0 - 2f) / cm)), bz = Math.Min(n - 1, (int)Math.Floor((z0 + span + 2f) / cm));
            int count = 0;
            for (int kz = az; kz <= bz; kz++)
                for (int kx = ax; kx <= bx; kx++)
                {
                    field.PlaceInto(kx, kz, objects);
                    foreach (var o in objects)
                    {
                        if (gone.Contains(o.Id)) continue;
                        columnList.Clear();
                        field.ContactsOf(o, columnList);
                        bool built = field.Library.Objects[o.Def].Kind == ObjectLibrary.Structure;
                        float foot = ObjectField.StampFoot(field.Library, o);
                        foreach (var c in columnList)
                        {
                            int sides = ObjectField.StampSides(field.Library, o, c);
                            if ((sides & ObjectField.StampFloor) != 0) { Stamp(data, x0, z0, texelM, c.X, c.Z, Math.Max(c.R, foot), built, ObjectField.StampChannel(field.Library, o, c)); count++; }
                            if ((sides & ObjectField.StampCeiling) != 0) { Stamp(data, x0, z0, texelM, c.X, c.Z, c.R, false, 3); count++; }
                        }
                        lushList.Clear();
                        field.LushOf(o, lushList);
                        foreach (var c in lushList) Ring(data, x0, z0, texelM, c.X, c.Z, c.R, c.Bottom);
                    }
                }
            return count;
        }

        static void Stamp(byte[] data, float x0, float z0, float texelM, float x, float z, float r, bool built, int channel)
        {
            float pad = built ? Mathf.Clamp(0.25f + 0.35f * r, 0.25f, 0.9f) : Mathf.Clamp(0.12f + 0.12f * r, 0.12f, 0.35f);
            float outer = r + pad, soft = pad + (built ? 0.3f : 0.25f) * r;
            int ia = Math.Max(0, (int)((x - outer - x0) / texelM)), ib = Math.Min(Res - 1, (int)((x + outer - x0) / texelM) + 1);
            int ja = Math.Max(0, (int)((z - outer - z0) / texelM)), jb = Math.Min(Res - 1, (int)((z + outer - z0) / texelM) + 1);
            for (int j = ja; j <= jb; j++)
            {
                float dz = z0 + (j + 0.5f) * texelM - z;
                int row = j * Res;
                for (int i = ia; i <= ib; i++)
                {
                    float dx = x0 + (i + 0.5f) * texelM - x;
                    float t = (outer - Mathf.Sqrt(dx * dx + dz * dz)) / soft;
                    if (t <= 0) continue;
                    t = t >= 1 ? 1 : t * t * (3 - 2 * t);
                    byte v = (byte)(t * 255f);
                    int k = 4 * (row + i) + channel;
                    if (v > data[k]) data[k] = v;
                }
            }
        }

        static void Ring(byte[] data, float x0, float z0, float texelM, float x, float z, float r, float width)
        {
            float half = 0.5f * width, soft = 0.45f * width, outer = r + half + soft;
            int ia = Math.Max(0, (int)((x - outer - x0) / texelM)), ib = Math.Min(Res - 1, (int)((x + outer - x0) / texelM) + 1);
            int ja = Math.Max(0, (int)((z - outer - z0) / texelM)), jb = Math.Min(Res - 1, (int)((z + outer - z0) / texelM) + 1);
            for (int j = ja; j <= jb; j++)
            {
                float dz = z0 + (j + 0.5f) * texelM - z;
                int row = j * Res;
                for (int i = ia; i <= ib; i++)
                {
                    float dx = x0 + (i + 0.5f) * texelM - x;
                    float t = (half + soft - Mathf.Abs(Mathf.Sqrt(dx * dx + dz * dz) - r)) / soft;
                    if (t <= 0) continue;
                    t = t >= 1 ? 1 : t * t * (3 - 2 * t);
                    byte v = (byte)(t * 255f);
                    int k = 4 * (row + i) + 1;
                    if (v > data[k]) data[k] = v;
                }
            }
        }

        static void Check(bool ok, string what)
        {
            log.Add((ok ? "ok   " : "FAIL ") + what);
            if (!ok) failed = true;
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
            string text = "ContactBurstCheck " + (code == 0 ? "passed" : "failed") + "\n" + string.Join("\n", log) + "\n" + string.Join("\n", GameStatus.Lines.Where(kv => kv.Key.Contains("contact") || kv.Key.Contains("object deltas")).OrderBy(kv => kv.Key).Select(kv => kv.Value));
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "out", "unity"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "contact-burst.txt"), text.Replace("\r", "") + "\n");
            Debug.Log(text);
            EditorApplication.Exit(code);
        }
    }
}
