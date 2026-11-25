using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Unity.Profiling;
using Stabby.Viewer;
using Stabby.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Stabby.Game
{
    public struct RegionColumns
    {
        public RegionGrid Grid;
        public UnsafeList<int> Starts;
        public UnsafeList<ObjectColumn> Items;
        public UnsafeList<ulong> Owners;

        public bool IsCreated => Starts.IsCreated;

        public bool Same(ColumnRegion r)
        {
            if (Grid.X0 != r.Grid.X0 || Grid.Z0 != r.Grid.Z0 || Grid.W != r.Grid.W || Grid.H != r.Grid.H || math.asint(Grid.Max) != math.asint(r.Grid.Max)) return false;
            if (Starts.Length != r.Starts.Length || Items.Length != r.Count || Owners.Length != r.Count) return false;
            for (int k = 0; k < r.Starts.Length; k++) if (Starts[k] != r.Starts[k]) return false;
            for (int k = 0; k < r.Count; k++)
            {
                ObjectColumn a = Items[k], b = r.Items[k];
                if (Owners[k] != r.Owners[k] || math.asint(a.X) != math.asint(b.X) || math.asint(a.Z) != math.asint(b.Z) || math.asint(a.R) != math.asint(b.R) ||
                    math.asint(a.Bottom) != math.asint(b.Bottom) || math.asint(a.Top) != math.asint(b.Top)) return false;
            }
            return true;
        }

        public void Dispose()
        {
            if (!IsCreated) return;
            Starts.Dispose();
            Items.Dispose();
            Owners.Dispose();
        }
    }

    public struct ColumnPart
    {
        public int Kind, Variant0;
        public bool Foliage, Rooted;
        public float OffsetX, OffsetY, OffsetZ, ScaleX, ScaleY, ScaleZ;
    }

    public struct ColumnVariant
    {
        public float Height, RootRadius, RootCentre, RootThick;
        public int Start, Count;
    }

    public struct ColumnShapes
    {
        public const int Local = 0, Log = 1, Post = 2;

        public UnsafeList<int2> Defs;
        public UnsafeList<ColumnPart> Parts;
        public UnsafeList<ColumnVariant> Variants;
        public UnsafeList<ObjectColumn> Templates;

        public bool IsCreated => Defs.IsCreated;

        public static ColumnShapes Of(ObjectLibrary lib, PartShapes shapes)
        {
            var s = new ColumnShapes
            {
                Defs = new UnsafeList<int2>(lib.Objects.Count, Allocator.Persistent),
                Parts = new UnsafeList<ColumnPart>(64, Allocator.Persistent),
                Variants = new UnsafeList<ColumnVariant>(256, Allocator.Persistent),
                Templates = new UnsafeList<ObjectColumn>(1024, Allocator.Persistent),
            };
            for (int d = 0; d < lib.Objects.Count; d++)
            {
                var def = lib.Objects[d];
                int start = s.Parts.Length;
                for (int pi = 0; pi < def.Parts.Count; pi++)
                {
                    var p = def.Parts[pi];
                    if (!lib.PartSolid(def, p)) continue;
                    var local = shapes.Columns[d][pi];
                    var cp = new ColumnPart
                    {
                        Kind = local != null ? Local : p.Shape == ObjectPart.Log ? Log : Post,
                        Variant0 = s.Variants.Length,
                        Foliage = ObjectPart.IsFoliage(p.Shape),
                        Rooted = p.Ends == ObjectPart.EndsRooted,
                        OffsetX = p.OffsetX, OffsetY = p.OffsetY, OffsetZ = p.OffsetZ,
                        ScaleX = p.ScaleX, ScaleY = p.ScaleY, ScaleZ = p.ScaleZ,
                    };
                    for (int v = 0; v < def.Variants; v++)
                    {
                        var cv = new ColumnVariant { Height = shapes.Heights[d][pi][v], Start = s.Templates.Length };
                        if (local != null)
                        {
                            foreach (var c in local[v]) s.Templates.Add(c);
                            cv.Count = local[v].Length;
                        }
                        if (cp.Kind == Log && cp.Rooted) ObjectMeshes.RootPlate(ObjectMeshes.Key(def.Id, pi, v), out cv.RootRadius, out cv.RootCentre, out cv.RootThick);
                        s.Variants.Add(cv);
                    }
                    s.Parts.Add(cp);
                }
                s.Defs.Add(new int2(start, s.Parts.Length - start));
            }
            return s;
        }

        public bool Solid(int def) => Defs[def].y > 0;

        public void Expand(in ObjectInstance o, ref UnsafeList<ObjectColumn> list)
        {
            var span = Defs[o.Def];
            for (int i = span.x; i < span.x + span.y; i++)
            {
                var p = Parts[i];
                var v = Variants[p.Variant0 + o.Variant];
                float tall = p.ScaleY * o.Size * v.Height;
                if (p.Kind == Local)
                {
                    float rs = math.min(p.ScaleX, p.ScaleZ) * o.Size;
                    for (int k = v.Start; k < v.Start + v.Count; k++)
                    {
                        var c = Templates[k];
                        Rotate(o, (p.OffsetX + c.X * p.ScaleX) * o.Size, (p.OffsetY + c.Bottom * p.ScaleY) * o.Size, (p.OffsetZ + c.Z * p.ScaleZ) * o.Size, out float lx, out float ly, out float lz);
                        list.Add(new ObjectColumn { X = lx + o.X, Z = lz + o.Z, R = c.R * rs, Bottom = ly + o.Y, Top = ly + o.Y + (c.Top - c.Bottom) * p.ScaleY * o.Size });
                    }
                }
                else if (p.Kind == Log)
                {
                    Rotate(o, p.OffsetX * o.Size, (p.OffsetY + 0.5f * p.ScaleY) * o.Size, p.OffsetZ * o.Size, out float cx, out float cy, out float cz);
                    cx += o.X; cy += o.Y; cz += o.Z;
                    float len = p.ScaleX * o.Size, rad = 0.5f * math.max(p.ScaleY, p.ScaleZ) * o.Size;
                    Rotate(o, 1, 0, 0, out float ux, out float uy, out float uz);
                    int count = math.max(1, math.min(16, (int)math.ceil(len / (2 * rad))));
                    for (int k = 0; k < count; k++)
                    {
                        float t = ((k + 0.5f) / count - 0.5f) * len;
                        float py = cy + uy * t;
                        list.Add(new ObjectColumn { X = cx + ux * t, Z = cz + uz * t, R = rad, Bottom = py - rad, Top = py + rad });
                    }
                    if (p.Rooted)
                    {
                        float rp = v.RootRadius, yc = v.RootCentre, thick = v.RootThick;
                        float t = -0.5f * len + (0.3f - 0.5f * thick) * rad;
                        Rotate(o, 0, 0, 1, out float wx, out float wy, out float wz);
                        for (int k = -1; k <= 1; k++)
                        {
                            float side = 0.55f * rp * rad * k;
                            float py = cy + uy * t + wy * side;
                            list.Add(new ObjectColumn { X = cx + ux * t + wx * side, Z = cz + uz * t + wz * side, R = 0.35f * rp * rad, Bottom = py - rad, Top = py + (yc + 0.9f * rp) * rad });
                        }
                    }
                }
                else
                {
                    Rotate(o, p.OffsetX * o.Size, p.OffsetY * o.Size, p.OffsetZ * o.Size, out float cx, out float cy, out float cz);
                    float rad = p.Foliage ? ObjectFoliage.TrunkRadius * 1.25f * math.min(p.ScaleX, p.ScaleZ) * o.Size : 0.5f * math.min(p.ScaleX, p.ScaleZ) * o.Size * 0.85f;
                    list.Add(new ObjectColumn { X = cx + o.X, Z = cz + o.Z, R = rad, Bottom = cy + o.Y, Top = cy + o.Y + tall });
                }
            }
        }

        static void Rotate(in ObjectInstance q, float vx, float vy, float vz, out float rx, out float ry, out float rz)
        {
            float tx = 2 * (q.Qy * vz - q.Qz * vy), ty = 2 * (q.Qz * vx - q.Qx * vz), tz = 2 * (q.Qx * vy - q.Qy * vx);
            rx = vx + q.Qw * tx + (q.Qy * tz - q.Qz * ty);
            ry = vy + q.Qw * ty + (q.Qz * tx - q.Qx * tz);
            rz = vz + q.Qw * tz + (q.Qx * ty - q.Qy * tx);
        }

        public void Dispose()
        {
            if (!IsCreated) return;
            Defs.Dispose();
            Parts.Dispose();
            Variants.Dispose();
            Templates.Dispose();
        }
    }

    [BurstCompile(FloatMode = FloatMode.Strict, CompileSynchronously = true)]
    public struct RegionBakeJob : IJob
    {
        [ReadOnly] public ColumnShapes Shapes;
        [ReadOnly] public UnsafeList<ObjectInstance> Objects;
        public NativeReference<RegionColumns> Out;

        public void Execute()
        {
            var columns = new UnsafeList<ObjectColumn>(Objects.Length * 2 + 1, Allocator.Temp);
            var owners = new UnsafeList<ulong>(Objects.Length * 2 + 1, Allocator.Temp);
            for (int i = 0; i < Objects.Length; i++)
            {
                var o = Objects[i];
                int before = columns.Length;
                Shapes.Expand(o, ref columns);
                for (int k = before; k < columns.Length; k++) owners.Add(o.Id);
            }
            Out.Value = Bin(ref columns, ref owners);
        }

        static int CellOf(float m) => (int)math.floor(m / ColumnRegion.CellM);

        static RegionColumns Bin(ref UnsafeList<ObjectColumn> columns, ref UnsafeList<ulong> owners)
        {
            int n = columns.Length;
            var r = new RegionColumns { Items = new UnsafeList<ObjectColumn>(n, Allocator.Persistent), Owners = new UnsafeList<ulong>(n, Allocator.Persistent) };
            if (n == 0)
            {
                r.Starts = new UnsafeList<int>(1, Allocator.Persistent);
                r.Starts.Add(0);
                return r;
            }
            int x0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, z1 = int.MinValue;
            float max = 0;
            for (int k = 0; k < n; k++)
            {
                var c = columns[k];
                int cx = CellOf(c.X), cz = CellOf(c.Z);
                x0 = math.min(x0, cx); x1 = math.max(x1, cx);
                z0 = math.min(z0, cz); z1 = math.max(z1, cz);
                max = math.max(max, c.R);
            }
            r.Grid = new RegionGrid { X0 = x0, Z0 = z0, W = x1 - x0 + 1, H = z1 - z0 + 1, Max = max };
            int cells = r.Grid.Cells;
            r.Starts = new UnsafeList<int>(cells + 1, Allocator.Persistent);
            r.Starts.Resize(cells + 1, NativeArrayOptions.ClearMemory);
            var at = new UnsafeList<int>(n, Allocator.Temp);
            for (int k = 0; k < n; k++)
            {
                int b = (CellOf(columns[k].Z) - z0) * r.Grid.W + CellOf(columns[k].X) - x0;
                at.Add(b);
                r.Starts[b + 1] = r.Starts[b + 1] + 1;
            }
            for (int b = 0; b < cells; b++) r.Starts[b + 1] = r.Starts[b + 1] + r.Starts[b];
            var fill = new UnsafeList<int>(cells + 1, Allocator.Temp);
            for (int b = 0; b <= cells; b++) fill.Add(r.Starts[b]);
            r.Items.Resize(n, NativeArrayOptions.UninitializedMemory);
            r.Owners.Resize(n, NativeArrayOptions.UninitializedMemory);
            for (int k = 0; k < n; k++)
            {
                int q = fill[at[k]];
                fill[at[k]] = q + 1;
                r.Items[q] = columns[k];
                r.Owners[q] = owners[k];
            }
            return r;
        }
    }

    public struct ObjectColumns : IComponentData, IColumnTable
    {
        public UnsafeList<RegionColumns> Regions;
        public float Size;
        public int Count;

        public float CellM => ColumnRegion.CellM;
        public float RegionM => Size;
        public int Across => Regions.IsCreated ? Count : 0;

        public bool Grid(int region, out RegionGrid g)
        {
            var b = Regions[region];
            g = b.IsCreated ? b.Grid : default;
            return b.IsCreated;
        }

        public int Start(int region, int cell) => Regions[region].Starts[cell];
        public ObjectColumn Column(int region, int k) => Regions[region].Items[k];
        public bool Resident(int region) => Regions.IsCreated && region < Regions.Length && Regions[region].IsCreated;

        public int Kill(ulong id)
        {
            int r = ObjectIds.Chunk(id), killed = 0;
            if (!Resident(r)) return 0;
            var data = Regions[r];
            for (int k = 0; k < data.Owners.Length; k++)
                if (data.Owners[k] == id && !Obstacles.IsDead(data.Items[k])) { data.Items[k] = Obstacles.Dead; killed++; }
            return killed;
        }

        public int Alive(ulong id)
        {
            int r = ObjectIds.Chunk(id), n = 0;
            if (!Resident(r)) return 0;
            var data = Regions[r];
            for (int k = 0; k < data.Owners.Length; k++)
                if (data.Owners[k] == id && !Obstacles.IsDead(data.Items[k])) n++;
            return n;
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.ClientSimulation)]
    public partial class ObjectColumnsSystem : SystemBase
    {
        sealed class Converted
        {
            public UnsafeList<ObjectInstance> Objects;
            public long Ms, Bytes;
        }

        struct Baking
        {
            public JobHandle Handle;
            public NativeReference<RegionColumns> Out;
            public long Started;
        }

        Task<ObjectField> fieldTask;
        Task<ColumnShapes> shapesTask;
        ColumnShapes shapes;
        UnsafeList<RegionColumns> regions;
        UnsafeList<UnsafeList<ObjectInstance>> instances;
        readonly Dictionary<int, Task<Converted>> converting = new Dictionary<int, Task<Converted>>();
        readonly Dictionary<int, Baking> baking = new Dictionary<int, Baking>();
        readonly List<int> resident = new List<int>();
        readonly List<int> wanted = new List<int>();
        readonly List<int> done = new List<int>();
        readonly List<float2> spots = new List<float2>();
        ObjectLibrary lib;
        float reach, freeReach;
        int lastColumns, columns, shown = -1, converted, baked;
        long lastMs, convertMs, convertBytes, bakeTicks;
        readonly MainCost cost = new MainCost();
        readonly CostSplit split = new CostSplit("converted", "install", "singleton", "free", "schedule");
        Baking warm;
        long warmTicks, firstSchedule, maxSchedule, singletonTicks;
        static readonly ProfilerMarker convertMarker = new ProfilerMarker("ObjectColumns.InstallConverted");
        static readonly ProfilerMarker installMarker = new ProfilerMarker("ObjectColumns.InstallBakes");
        static readonly ProfilerMarker singletonMarker = new ProfilerMarker("ObjectColumns.GetSingleton");
        static readonly ProfilerMarker freeMarker = new ProfilerMarker("ObjectColumns.Free");
        static readonly ProfilerMarker wantMarker = new ProfilerMarker("ObjectColumns.Schedule");
        static readonly ProfilerMarker warmMarker = new ProfilerMarker("ObjectColumns.WarmUp");

        public ObjectField Field { get; private set; }
        public ObjectLibrary Library => lib;
        public float Reach => reach;
        public bool Settled => Field != null && shapes.IsCreated && baking.Count == 0 && converting.Count == 0;

        protected override void OnCreate()
        {
            lib = ContentFiles.Objects(ContentFiles.Biomes());
            reach = ObjectRegions.Reach(lib);
            freeReach = ObjectRegions.FreeReach(reach);
            var c = cost;
            string name = World.Name;
            GameStatus.Live[name + "~objects cost"] = () => $"{name}: {c.Text("region arrivals")}; {converted} chunks converted to native once at {(converted == 0 ? 0 : (double)convertMs / converted):F1} ms and {(converted == 0 ? 0 : convertBytes / 1024.0 / converted):F0} KB GC each on workers; {baked} Burst region bakes, {(baked == 0 ? 0 : bakeTicks * 1000.0 / Stopwatch.Frequency / baked):F1} ms schedule to install; {split.Text()}; warm-up schedule {Ms(warmTicks):F2} ms, first bake schedule {Ms(firstSchedule):F2} ms, later schedules max {Ms(maxSchedule):F2} ms";
        }

        protected override void OnDestroy()
        {
            foreach (var b in baking.Values)
            {
                b.Handle.Complete();
                b.Out.Value.Dispose();
                b.Out.Dispose();
            }
            baking.Clear();
            if (warm.Out.IsCreated)
            {
                warm.Handle.Complete();
                warm.Out.Value.Dispose();
                warm.Out.Dispose();
            }
            foreach (var t in converting.Values) Owned.Release(t, c => c.Objects.Dispose());
            converting.Clear();
            Owned.Release(shapesTask, s => s.Dispose());
            shapes.Dispose();
            if (instances.IsCreated)
            {
                for (int k = 0; k < instances.Length; k++) if (instances[k].IsCreated) instances[k].Dispose();
                instances.Dispose();
            }
            if (!regions.IsCreated) return;
            for (int k = 0; k < regions.Length; k++) if (regions[k].IsCreated) regions[k].Dispose();
            regions.Dispose();
        }

        (GeneratedWorld, GroundParts, WorldStore.Entry) MapReady()
        {
            if (World.IsServer())
            {
                var server = World.GetExistingSystemManaged<ServerMapSystem>();
                return (server.Map, server.Parts, server.Shared);
            }
            var client = World.GetExistingSystemManaged<ClientMapSystem>();
            return client.Map != null && client.Parts != null && SystemAPI.HasSingleton<Ground>() ? (client.Map, client.Parts, client.Shared) : default;
        }

        protected override void OnUpdate()
        {
            if (Field == null)
            {
                if (fieldTask == null)
                {
                    var (w, parts, entry) = MapReady();
                    if (w == null || parts == null) return;
                    var objects = lib;
                    System.Func<ObjectField> make = () => new ObjectField(w, parts.Detail, parts.Edits, objects);
                    fieldTask = entry == null ? Task.Run(make) : WorldStore.Objects(entry, make);
                    return;
                }
                if (!fieldTask.IsCompleted) return;
                if (fieldTask.IsFaulted) { GameStatus.Failure = fieldTask.Exception.ToString(); fieldTask = null; Enabled = false; return; }
                Field = fieldTask.Result;
                fieldTask = null;
                var field = Field;
                var library = lib;
                shapesTask = Task.Run(() => ColumnShapes.Of(library, field.Shapes));
                int across = Field.ChunksAcross;
                regions = new UnsafeList<RegionColumns>(across * across, Allocator.Persistent);
                regions.Resize(across * across, NativeArrayOptions.ClearMemory);
                instances = new UnsafeList<UnsafeList<ObjectInstance>>(across * across, Allocator.Persistent);
                instances.Resize(across * across, NativeArrayOptions.ClearMemory);
                EntityManager.CreateSingleton(new ObjectColumns { Regions = regions, Size = Field.ChunkMeters, Count = across });
            }
            if (!shapes.IsCreated)
            {
                if (!shapesTask.IsCompleted) return;
                if (shapesTask.IsFaulted) { GameStatus.Failure = shapesTask.Exception.ToString(); Enabled = false; return; }
                shapes = shapesTask.Result;
                shapesTask = null;
                warm.Out = new NativeReference<RegionColumns>(Allocator.Persistent);
                long w0 = Stopwatch.GetTimestamp();
                using (warmMarker.Auto()) warm.Handle = new RegionBakeJob { Shapes = shapes, Out = warm.Out }.Schedule();
                warmTicks = Stopwatch.GetTimestamp() - w0;
                JobHandle.ScheduleBatchedJobs();
                return;
            }
            if (warm.Out.IsCreated && warm.Handle.IsCompleted)
            {
                warm.Handle.Complete();
                warm.Out.Value.Dispose();
                warm.Out.Dispose();
            }

            spots.Clear();
            foreach (var t in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<Player>())
                spots.Add(t.ValueRO.Position.xz);
            if (SystemAPI.TryGetSingleton<SpawnPoint>(out var spawn)) spots.Add(spawn.XZ);

            cost.Begin();
            long t0 = Stopwatch.GetTimestamp();
            using (convertMarker.Auto()) InstallConverted();
            long t1 = Stopwatch.GetTimestamp();
            int installed;
            using (installMarker.Auto()) installed = Install();
            long t2 = Stopwatch.GetTimestamp();
            using (freeMarker.Auto()) Free();
            long t3 = Stopwatch.GetTimestamp();
            int started;
            using (wantMarker.Auto()) started = Want();
            long t4 = Stopwatch.GetTimestamp();
            cost.End(installed + started > 0, installed);
            if (installed + started > 0) split.Add(t1 - t0, t2 - t1 - singletonTicks, singletonTicks, t3 - t2, t4 - t3);
            singletonTicks = 0;
            Status();
        }

        static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        bool Near(int region, float within)
        {
            foreach (var p in spots)
                if (ObjectRegions.Distance(Field.ChunksAcross, Field.ChunkMeters, region, p.x, p.y) <= within) return true;
            return false;
        }

        ObjectDeltas Deltas => World.IsServer() ? World.GetExistingSystemManaged<ServerObjectDeltaSystem>()?.Deltas : World.GetExistingSystemManaged<ClientObjectDeltaSystem>()?.Deltas;

        void InstallConverted()
        {
            if (converting.Count == 0) return;
            done.Clear();
            done.AddRange(converting.Keys);
            foreach (int r in done)
            {
                var task = converting[r];
                if (!task.IsCompleted) continue;
                converting.Remove(r);
                if (task.IsFaulted) { GameStatus.Failure = task.Exception.ToString(); continue; }
                var c = task.Result;
                instances[r] = c.Objects;
                converted++;
                convertMs += c.Ms;
                convertBytes += c.Bytes;
            }
        }

        int Install()
        {
            int installed = 0;
            if (baking.Count == 0) return 0;
            done.Clear();
            done.AddRange(baking.Keys);
            ObjectColumns oc = default;
            bool haveColumns = false;
            ObjectDeltas deltas = null;
            foreach (int r in done)
            {
                var b = baking[r];
                if (!b.Handle.IsCompleted) continue;
                if (!haveColumns)
                {
                    long s0 = Stopwatch.GetTimestamp();
                    using (singletonMarker.Auto()) oc = SystemAPI.GetSingletonRW<ObjectColumns>().ValueRO;
                    singletonTicks = Stopwatch.GetTimestamp() - s0;
                    deltas = Deltas;
                    haveColumns = true;
                }
                b.Handle.Complete();
                baking.Remove(r);
                var region = b.Out.Value;
                b.Out.Dispose();
                long ticks = Stopwatch.GetTimestamp() - b.Started;
                baked++;
                bakeTicks += ticks;
                if (!Near(r, freeReach)) { region.Dispose(); continue; }
                regions[r] = region;
                resident.Add(r);
                columns += region.Items.Length;
                lastColumns = region.Items.Length;
                lastMs = ticks * 1000 / Stopwatch.Frequency;
                installed++;
                if (deltas != null) foreach (ulong id in deltas.InChunk(r)) oc.Kill(id);
            }
            return installed;
        }

        void Free()
        {
            bool synced = false;
            for (int k = resident.Count - 1; k >= 0; k--)
            {
                int r = resident[k];
                if (Near(r, freeReach)) continue;
                if (!synced) { SystemAPI.GetSingletonRW<ObjectColumns>(); synced = true; }
                columns -= regions[r].Items.Length;
                regions[r].Dispose();
                regions[r] = default;
                resident.RemoveAt(k);
            }
        }

        int Want()
        {
            int started = 0;
            wanted.Clear();
            foreach (var p in spots) ObjectRegions.Near(Field.ChunksAcross, Field.ChunkMeters, p.x, p.y, reach, wanted);
            var field = Field;
            var table = shapes;
            foreach (int r in wanted)
            {
                if (regions[r].IsCreated || baking.ContainsKey(r) || converting.ContainsKey(r)) continue;
                started++;
                if (!instances[r].IsCreated)
                {
                    int region = r;
                    converting[r] = Task.Run(() => Convert(field, table, region));
                    continue;
                }
                var output = new NativeReference<RegionColumns>(Allocator.Persistent);
                var job = new RegionBakeJob { Shapes = shapes, Objects = instances[r], Out = output };
                long s0 = Stopwatch.GetTimestamp();
                var handle = job.Schedule();
                long scheduled = Stopwatch.GetTimestamp() - s0;
                if (baked + baking.Count == 0) firstSchedule = scheduled;
                else maxSchedule = System.Math.Max(maxSchedule, scheduled);
                baking[r] = new Baking { Handle = handle, Out = output, Started = Stopwatch.GetTimestamp() };
            }
            if (baking.Count > 0) JobHandle.ScheduleBatchedJobs();
            return started;
        }

        [System.ThreadStatic] static List<ObjectInstance> scratch;

        static Converted Convert(ObjectField field, ColumnShapes table, int index)
        {
            var sw = Stopwatch.StartNew();
            long b0 = System.GC.GetAllocatedBytesForCurrentThread();
            var list = scratch ??= new List<ObjectInstance>();
            int across = field.ChunksAcross;
            field.SolidInto(index % across, index / across, list);
            int solid = 0;
            foreach (var o in list) if (table.Solid(o.Def)) solid++;
            var native = new UnsafeList<ObjectInstance>(solid, Allocator.Persistent);
            foreach (var o in list) if (table.Solid(o.Def)) native.AddNoResize(o);
            list.Clear();
            return new Converted { Objects = native, Ms = sw.ElapsedMilliseconds, Bytes = System.GC.GetAllocatedBytesForCurrentThread() - b0 };
        }

        public string CompareManaged(out int count, out int items, out int differ)
        {
            count = items = differ = 0;
            if (Field == null || !regions.IsCreated) return $"{World.Name}: no columns";
            var deltas = Deltas;
            for (int r = 0; r < regions.Length; r++)
            {
                if (!regions[r].IsCreated) continue;
                var m = ColumnRegion.Build(Field, r);
                if (deltas != null) foreach (ulong id in deltas.InChunk(r)) m.Kill(id);
                count++;
                items += m.Count;
                if (!regions[r].Same(m)) differ++;
            }
            return $"{World.Name}: {count} regions, {items} columns, {differ} differ";
        }

        void Status()
        {
            int key = resident.Count * 7919 + (baking.Count + converting.Count) * 31 + columns;
            if (key == shown) return;
            shown = key;
            GameStatus.Lines[World.Name + "~objects"] = $"{World.Name}: {resident.Count} object regions, {columns} columns, {baking.Count + converting.Count} building, reach {reach:F0} m; last region {lastColumns} columns in {lastMs} ms";
        }
    }
}
