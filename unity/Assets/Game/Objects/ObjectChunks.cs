using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Stabby.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Stabby.Game
{
    public struct ContactStamp
    {
        public float X, Z, R;
        public byte Built, Channel;
        public ulong Id;
    }

    public struct ContactRing
    {
        public float X, Z, R, Width;
        public ulong Id;
    }

    public struct ContactChunk : IDisposable
    {
        public UnsafeList<ContactStamp> Stamps;
        public UnsafeList<ContactRing> Rings;

        public void Dispose()
        {
            if (Stamps.IsCreated) Stamps.Dispose();
            if (Rings.IsCreated) Rings.Dispose();
        }
    }

    public struct ReachRow
    {
        public ObjectInstance Object;
        public float Reach;
    }

    public struct RemoveAsk
    {
        public ulong Id;
        public float3 At;
    }

    public struct ContactWindow
    {
        public float X0, Z0, TexelM;
        public int Ax, Bx, Az, Bz;

        public static ContactWindow Around(ObjectField field, int res, float cx, float cz, float texelM)
        {
            float span = res * texelM;
            float x0 = UnityEngine.Mathf.Floor((cx - 0.5f * span) / texelM) * texelM, z0 = UnityEngine.Mathf.Floor((cz - 0.5f * span) / texelM) * texelM;
            float cm = field.ChunkMeters;
            int n = field.ChunksAcross;
            return new ContactWindow
            {
                X0 = x0, Z0 = z0, TexelM = texelM,
                Ax = Math.Max(0, (int)Math.Floor((x0 - 2f) / cm)), Bx = Math.Min(n - 1, (int)Math.Floor((x0 + span + 2f) / cm)),
                Az = Math.Max(0, (int)Math.Floor((z0 - 2f) / cm)), Bz = Math.Min(n - 1, (int)Math.Floor((z0 + span + 2f) / cm)),
            };
        }

        public bool Holds(int chunk, int across)
        {
            int cx = chunk % across, cz = chunk / across;
            return cx >= Ax && cx <= Bx && cz >= Az && cz <= Bz;
        }
    }

    public struct JobSlot
    {
        JobHandle handle;

        public bool Busy { get; private set; }
        public bool Done => Busy && handle.IsCompleted;

        public void Start(JobHandle h)
        {
            handle = h;
            Busy = true;
        }

        public void Finish()
        {
            if (!Busy) return;
            handle.Complete();
            Busy = false;
        }
    }

    public static class ObjectChunks
    {
        [ThreadStatic] static List<ObjectColumn> columnScratch, lushScratch;

        public static ContactChunk Contacts(ObjectField field, int cx, int cz)
        {
            var objects = ListPool.Rent<ObjectInstance>();
            try { return Contacts(field, cx, cz, objects); }
            finally { ListPool.Return(objects); }
        }

        static ContactChunk Contacts(ObjectField field, int cx, int cz, List<ObjectInstance> objects)
        {
            var columns = columnScratch ??= new List<ObjectColumn>();
            var lush = lushScratch ??= new List<ObjectColumn>();
            field.PlaceInto(cx, cz, objects);
            var chunk = new ContactChunk { Stamps = new UnsafeList<ContactStamp>(objects.Count, Allocator.Persistent), Rings = new UnsafeList<ContactRing>(0, Allocator.Persistent) };
            foreach (var o in objects)
            {
                bool cave = ObjectField.IsCave(field.Library.Objects[o.Def]);
                columns.Clear();
                field.ContactsOf(o, columns);
                bool built = field.Library.Objects[o.Def].Kind == ObjectLibrary.Structure;
                float foot = built ? Structures.FootReach * o.Size : 0f;
                foreach (var c in columns)
                {
                    int sides = ObjectField.StampSides(field.Library, o, c);
                    if ((sides & ObjectField.StampFloor) != 0)
                        chunk.Stamps.Add(new ContactStamp { X = c.X, Z = c.Z, R = Math.Max(c.R, foot), Built = (byte)(built ? 1 : 0), Channel = (byte)(cave ? 2 : 0), Id = o.Id });
                    if ((sides & ObjectField.StampCeiling) != 0)
                        chunk.Stamps.Add(new ContactStamp { X = c.X, Z = c.Z, R = c.R, Channel = 3, Id = o.Id });
                }
                lush.Clear();
                field.LushOf(o, lush);
                foreach (var c in lush) chunk.Rings.Add(new ContactRing { X = c.X, Z = c.Z, R = c.R, Width = c.Bottom, Id = o.Id });
            }
            return chunk;
        }

        public static UnsafeList<ReachRow> Reach(ObjectField field, int chunk)
        {
            int across = field.ChunksAcross;
            var rows = new UnsafeList<ReachRow>(0, Allocator.Persistent);
            foreach (var o in field.Chunk(chunk % across, chunk / across))
                if (field.Library.IsInteractive(o.Def))
                    rows.Add(new ReachRow { Object = o, Reach = field.Library.Objects[o.Def].ReachM + field.Footprint(o.Def) * o.Size });
            return rows;
        }

        public static NativeArray<byte> Interactive(ObjectField field)
        {
            var flags = new NativeArray<byte>(field.Library.Objects.Count, Allocator.Persistent);
            for (int d = 0; d < flags.Length; d++) flags[d] = (byte)(field.Library.IsInteractive(d) ? 1 : 0);
            return flags;
        }
    }

    public static class ReachLookup
    {
        public static bool Find(in NativeHashMap<int, UnsafeList<ReachRow>> chunks, int across, ulong id, out ReachRow found)
        {
            found = default;
            int chunk = ObjectIds.Chunk(id);
            if (chunk >= across * across || !chunks.TryGetValue(chunk, out var rows)) return false;
            for (int k = 0; k < rows.Length; k++)
                if (rows[k].Object.Id == id) { found = rows[k]; return true; }
            return false;
        }
    }

    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true)]
    public struct ContactSplatJob : IJob
    {
        public const int Res = ContactFieldSystem.Res;

        [ReadOnly] public NativeHashMap<int, ContactChunk> Chunks;
        [ReadOnly] public NativeHashSet<ulong> Gone;
        public NativeArray<byte> Data;
        public NativeReference<int> Columns;
        public ContactWindow Window;
        public int Across;

        public void Execute()
        {
            for (int k = 0; k < Data.Length; k++) Data[k] = 0;
            int count = 0;
            for (int kz = Window.Az; kz <= Window.Bz; kz++)
                for (int kx = Window.Ax; kx <= Window.Bx; kx++)
                {
                    if (!Chunks.TryGetValue(kz * Across + kx, out var c)) continue;
                    for (int s = 0; s < c.Stamps.Length; s++)
                    {
                        var st = c.Stamps[s];
                        if (Gone.Contains(st.Id)) continue;
                        Stamp(st.X, st.Z, st.R, st.Built != 0, st.Channel);
                        count++;
                    }
                    for (int s = 0; s < c.Rings.Length; s++)
                    {
                        var rg = c.Rings[s];
                        if (!Gone.Contains(rg.Id)) Ring(rg.X, rg.Z, rg.R, rg.Width);
                    }
                }
            Columns.Value = count;
        }

        void Stamp(float x, float z, float r, bool built, int channel)
        {
            double x0 = Window.X0, z0 = Window.Z0, texelM = Window.TexelM, xd = x, zd = z, rd = r;
            float pad = built ? (float)math.clamp(0.25 + (double)0.35f * rd, 0.25, (double)0.9f) : (float)math.clamp((double)0.12f + (double)0.12f * rd, (double)0.12f, (double)0.35f);
            float outer = (float)(rd + pad), soft = (float)(pad + (built ? (double)0.3f : 0.25) * rd);
            int ia = math.max(0, (int)((xd - outer - x0) / texelM)), ib = math.min(Res - 1, (int)((xd + outer - x0) / texelM) + 1);
            int ja = math.max(0, (int)((zd - outer - z0) / texelM)), jb = math.min(Res - 1, (int)((zd + outer - z0) / texelM) + 1);
            for (int j = ja; j <= jb; j++)
            {
                float dz = (float)(z0 + (j + 0.5) * texelM - zd);
                int row = j * Res;
                for (int i = ia; i <= ib; i++)
                {
                    float dx = (float)(x0 + (i + 0.5) * texelM - xd);
                    float t = (float)((outer - (float)math.sqrt((double)dx * dx + (double)dz * dz)) / (double)soft);
                    if (t <= 0) continue;
                    t = t >= 1 ? 1 : (float)((double)t * t * (3 - 2 * (double)t));
                    byte v = (byte)(t * 255.0);
                    int k = 4 * (row + i) + channel;
                    if (v > Data[k]) Data[k] = v;
                }
            }
        }

        void Ring(float x, float z, float r, float width)
        {
            double x0 = Window.X0, z0 = Window.Z0, texelM = Window.TexelM, xd = x, zd = z, rd = r, wd = width;
            float half = (float)(0.5 * wd), soft = (float)((double)0.45f * wd), outer = (float)(rd + half + soft);
            int ia = math.max(0, (int)((xd - outer - x0) / texelM)), ib = math.min(Res - 1, (int)((xd + outer - x0) / texelM) + 1);
            int ja = math.max(0, (int)((zd - outer - z0) / texelM)), jb = math.min(Res - 1, (int)((zd + outer - z0) / texelM) + 1);
            for (int j = ja; j <= jb; j++)
            {
                float dz = (float)(z0 + (j + 0.5) * texelM - zd);
                int row = j * Res;
                for (int i = ia; i <= ib; i++)
                {
                    float dx = (float)(x0 + (i + 0.5) * texelM - xd);
                    float t = (float)(((double)half + soft - math.abs((float)math.sqrt((double)dx * dx + (double)dz * dz) - rd)) / soft);
                    if (t <= 0) continue;
                    t = t >= 1 ? 1 : (float)((double)t * t * (3 - 2 * (double)t));
                    byte v = (byte)(t * 255.0);
                    int k = 4 * (row + i) + 1;
                    if (v > Data[k]) Data[k] = v;
                }
            }
        }
    }

    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true)]
    public struct RemoveCheckJob : IJob
    {
        [ReadOnly] public NativeHashMap<int, UnsafeList<ReachRow>> Chunks;
        [ReadOnly] public NativeArray<byte> Interactive;
        [ReadOnly] public NativeHashSet<ulong> Removed;
        [ReadOnly] public NativeArray<RemoveAsk> Asks;
        public NativeArray<byte> Results;
        public int Across;

        public void Execute()
        {
            for (int k = 0; k < Asks.Length; k++) Results[k] = (byte)Check(Asks[k]);
        }

        RemoveResult Check(RemoveAsk a)
        {
            int chunk = ObjectIds.Chunk(a.Id), def = ObjectIds.Def(a.Id);
            if (chunk >= Across * Across) return RemoveResult.NoChunk;
            if (def >= Interactive.Length) return RemoveResult.NoObject;
            if (Interactive[def] == 0) return RemoveResult.NotInteractive;
            if (!ReachLookup.Find(Chunks, Across, a.Id, out var found)) return RemoveResult.NoObject;
            if (Removed.Contains(a.Id)) return RemoveResult.AlreadyRemoved;
            float dx = found.Object.X - a.At.x, dy = found.Object.Y - a.At.y, dz = found.Object.Z - a.At.z;
            return dx * dx + dy * dy + dz * dz <= found.Reach * found.Reach ? RemoveResult.Ok : RemoveResult.OutOfReach;
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    public struct ResolveJob : IJob
    {
        [ReadOnly] public NativeHashMap<int, UnsafeList<ReachRow>> Chunks;
        [ReadOnly] public NativeArray<ulong> Ids;
        public NativeArray<ObjectInstance> Found;
        public NativeArray<byte> Ok;
        public int Across;

        public void Execute()
        {
            for (int k = 0; k < Ids.Length; k++)
            {
                bool ok = ReachLookup.Find(Chunks, Across, Ids[k], out var row);
                Ok[k] = (byte)(ok ? 1 : 0);
                Found[k] = row.Object;
            }
        }
    }

    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true)]
    public struct InReachJob : IJob
    {
        [ReadOnly] public NativeHashMap<int, UnsafeList<ReachRow>> Chunks;
        public NativeList<ulong> Ids;
        public NativeList<float> Dist;
        public float3 At;
        public int Cx0, Cx1, Cz0, Cz1, Across;

        public static InReachJob Around(ObjectField field, float3 p)
        {
            float cm = field.ChunkMeters;
            int across = field.ChunksAcross;
            return new InReachJob
            {
                At = p, Across = across,
                Cx0 = Math.Max(0, (int)Math.Floor(p.x / cm) - 1), Cx1 = Math.Min(across - 1, (int)Math.Floor(p.x / cm) + 1),
                Cz0 = Math.Max(0, (int)Math.Floor(p.z / cm) - 1), Cz1 = Math.Min(across - 1, (int)Math.Floor(p.z / cm) + 1),
            };
        }

        public void Chunks9(List<int> into)
        {
            for (int cz = Cz0; cz <= Cz1; cz++)
                for (int cx = Cx0; cx <= Cx1; cx++) into.Add(cz * Across + cx);
        }

        public void Execute()
        {
            Ids.Clear();
            Dist.Clear();
            for (int cz = Cz0; cz <= Cz1; cz++)
                for (int cx = Cx0; cx <= Cx1; cx++)
                {
                    if (!Chunks.TryGetValue(cz * Across + cx, out var rows)) continue;
                    for (int k = 0; k < rows.Length; k++)
                    {
                        var o = rows[k].Object;
                        float dx = o.X - At.x, dy = o.Y - At.y, dz = o.Z - At.z, d = dx * dx + dy * dy + dz * dz;
                        float reach = rows[k].Reach;
                        if (d > reach * reach) continue;
                        int at = Dist.Length;
                        while (at > 0 && Dist[at - 1] > d) at--;
                        Dist.Add(0);
                        Ids.Add(0);
                        for (int m = Dist.Length - 1; m > at; m--)
                        {
                            Dist[m] = Dist[m - 1];
                            Ids[m] = Ids[m - 1];
                        }
                        Dist[at] = d;
                        Ids[at] = o.Id;
                    }
                }
        }
    }

    public sealed class ReachTables : IDisposable
    {
        public NativeHashMap<int, UnsafeList<ReachRow>> Chunks = new NativeHashMap<int, UnsafeList<ReachRow>>(64, Allocator.Persistent);
        public NativeArray<byte> Interactive;
        public NativeHashSet<ulong> Removed = new NativeHashSet<ulong>(64, Allocator.Persistent);
        public NativeList<ulong> RemovedOrder = new NativeList<ulong>(64, Allocator.Persistent);
        Task<List<(int chunk, UnsafeList<ReachRow> rows)>> building;
        readonly List<int> want = new List<int>();

        public int Across { get; private set; }

        public bool Ready(ObjectField field, List<int> chunks)
        {
            if (!Interactive.IsCreated)
            {
                Interactive = ObjectChunks.Interactive(field);
                Across = field.ChunksAcross;
            }
            if (building != null)
            {
                if (!building.IsCompleted) return false;
                if (building.IsFaulted) GameStatus.Failure = building.Exception.ToString();
                else foreach (var (c, rows) in building.Result) Chunks[c] = rows;
                building = null;
            }
            want.Clear();
            foreach (int c in chunks)
                if (c >= 0 && c < Across * Across && !Chunks.ContainsKey(c) && !want.Contains(c)) want.Add(c);
            if (want.Count == 0) return true;
            var list = want.ToArray();
            building = Task.Run(() =>
            {
                var made = new List<(int, UnsafeList<ReachRow>)>(list.Length);
                foreach (int c in list) made.Add((c, ObjectChunks.Reach(field, c)));
                return made;
            });
            return false;
        }

        public bool Remove(ulong id)
        {
            if (!Removed.Add(id)) return false;
            RemovedOrder.Add(id);
            return true;
        }

        public void Dispose()
        {
            Owned.Release(building, made => { foreach (var (_, rows) in made) rows.Dispose(); });
            building = null;
            foreach (var kv in Chunks) kv.Value.Dispose();
            Chunks.Dispose();
            if (Interactive.IsCreated) Interactive.Dispose();
            Removed.Dispose();
            RemovedOrder.Dispose();
        }
    }
}
