using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Stabby.Viewer;
using Stabby.World;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;

namespace Stabby.Game
{
    public struct RegionColumnData
    {
        public RegionGrid Grid;
        public BlobArray<int> Starts;
        public BlobArray<ObjectColumn> Items;
        public BlobArray<ulong> Owners;
    }

    public struct ObjectColumns : IComponentData, IColumnTable
    {
        public UnsafeList<BlobAssetReference<RegionColumnData>> Regions;
        public float Size;
        public int Count;

        public float CellM => ColumnRegion.CellM;
        public float RegionM => Size;
        public int Across => Regions.IsCreated ? Count : 0;

        public bool Grid(int region, out RegionGrid g)
        {
            var b = Regions[region];
            g = b.IsCreated ? b.Value.Grid : default;
            return b.IsCreated;
        }

        public int Start(int region, int cell) => Regions[region].Value.Starts[cell];
        public ObjectColumn Column(int region, int k) => Regions[region].Value.Items[k];
        public bool Resident(int region) => Regions.IsCreated && region < Regions.Length && Regions[region].IsCreated;

        public int Kill(ulong id)
        {
            int r = ObjectIds.Chunk(id), killed = 0;
            if (!Resident(r)) return 0;
            ref var data = ref Regions[r].Value;
            for (int k = 0; k < data.Owners.Length; k++)
                if (data.Owners[k] == id && !Obstacles.IsDead(data.Items[k])) { data.Items[k] = Obstacles.Dead; killed++; }
            return killed;
        }

        public int Alive(ulong id)
        {
            int r = ObjectIds.Chunk(id), n = 0;
            if (!Resident(r)) return 0;
            ref var data = ref Regions[r].Value;
            for (int k = 0; k < data.Owners.Length; k++)
                if (data.Owners[k] == id && !Obstacles.IsDead(data.Items[k])) n++;
            return n;
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.ClientSimulation)]
    public partial class ObjectColumnsSystem : SystemBase
    {
        sealed class Built
        {
            public BlobAssetReference<RegionColumnData> Blob;
            public int Columns;
            public long Ms;
        }

        Task<ObjectField> fieldTask;
        UnsafeList<BlobAssetReference<RegionColumnData>> regions;
        readonly Dictionary<int, Task<Built>> building = new Dictionary<int, Task<Built>>();
        readonly List<int> resident = new List<int>();
        readonly List<int> wanted = new List<int>();
        readonly List<int> done = new List<int>();
        readonly List<Unity.Mathematics.float2> spots = new List<Unity.Mathematics.float2>();
        ObjectLibrary lib;
        float reach, freeReach;
        int lastColumns, columns, shown = -1;
        long lastMs;

        public ObjectField Field { get; private set; }
        public ObjectLibrary Library => lib;
        public float Reach => reach;

        protected override void OnCreate()
        {
            lib = ContentFiles.Objects(ContentFiles.Biomes());
            reach = ObjectRegions.Reach(lib);
            freeReach = ObjectRegions.FreeReach(reach);
        }

        protected override void OnDestroy()
        {
            foreach (var t in building.Values) Owned.Release(t, b => b.Blob.Dispose());
            building.Clear();
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
                int across = Field.ChunksAcross;
                regions = new UnsafeList<BlobAssetReference<RegionColumnData>>(across * across, Allocator.Persistent);
                regions.Resize(across * across, NativeArrayOptions.ClearMemory);
                EntityManager.CreateSingleton(new ObjectColumns { Regions = regions, Size = Field.ChunkMeters, Count = across });
            }

            spots.Clear();
            foreach (var t in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<Player>())
                spots.Add(t.ValueRO.Position.xz);
            if (SystemAPI.TryGetSingleton<SpawnPoint>(out var spawn)) spots.Add(spawn.XZ);

            Install();
            Free();
            Want();
            Status();
        }

        bool Near(int region, float within)
        {
            foreach (var p in spots)
                if (ObjectRegions.Distance(Field.ChunksAcross, Field.ChunkMeters, region, p.x, p.y) <= within) return true;
            return false;
        }

        void Install()
        {
            done.Clear();
            done.AddRange(building.Keys);
            if (done.Count == 0) return;
            var oc = SystemAPI.GetSingleton<ObjectColumns>();
            var deltas = World.IsServer() ? World.GetExistingSystemManaged<ServerObjectDeltaSystem>()?.Deltas : World.GetExistingSystemManaged<ClientObjectDeltaSystem>()?.Deltas;
            foreach (int r in done)
            {
                var task = building[r];
                if (!task.IsCompleted) continue;
                building.Remove(r);
                if (task.IsFaulted) { GameStatus.Failure = task.Exception.ToString(); continue; }
                var b = task.Result;
                if (!Near(r, freeReach)) { b.Blob.Dispose(); continue; }
                regions[r] = b.Blob;
                resident.Add(r);
                columns += b.Columns;
                lastColumns = b.Columns;
                lastMs = b.Ms;
                if (deltas != null) foreach (ulong id in deltas.InChunk(r)) oc.Kill(id);
            }
        }

        void Free()
        {
            for (int k = resident.Count - 1; k >= 0; k--)
            {
                int r = resident[k];
                if (Near(r, freeReach)) continue;
                columns -= regions[r].Value.Items.Length;
                regions[r].Dispose();
                regions[r] = default;
                resident.RemoveAt(k);
            }
        }

        void Want()
        {
            wanted.Clear();
            foreach (var p in spots) ObjectRegions.Near(Field.ChunksAcross, Field.ChunkMeters, p.x, p.y, reach, wanted);
            var field = Field;
            foreach (int r in wanted)
            {
                if (regions[r].IsCreated || building.ContainsKey(r)) continue;
                int region = r;
                building[r] = Task.Run(() => Bake(field, region));
            }
        }

        static Built Bake(ObjectField field, int index)
        {
            var sw = Stopwatch.StartNew();
            var r = ColumnRegion.Build(field, index);
            var b = new BlobBuilder(Allocator.Persistent);
            try
            {
                ref var root = ref b.ConstructRoot<RegionColumnData>();
                root.Grid = r.Grid;
                var starts = b.Allocate(ref root.Starts, r.Starts.Length);
                for (int k = 0; k < r.Starts.Length; k++) starts[k] = r.Starts[k];
                var items = b.Allocate(ref root.Items, r.Count);
                var owners = b.Allocate(ref root.Owners, r.Count);
                for (int k = 0; k < r.Count; k++) { items[k] = r.Items[k]; owners[k] = r.Owners[k]; }
                var blob = b.CreateBlobAssetReference<RegionColumnData>(Allocator.Persistent);
                return new Built { Blob = blob, Columns = r.Count, Ms = sw.ElapsedMilliseconds };
            }
            finally
            {
                b.Dispose();
            }
        }

        void Status()
        {
            int key = resident.Count * 7919 + building.Count * 31 + columns;
            if (key == shown) return;
            shown = key;
            GameStatus.Lines[World.Name + "~objects"] = $"{World.Name}: {resident.Count} object regions, {columns} columns, {building.Count} building, reach {reach:F0} m; last region {lastColumns} columns in {lastMs} ms";
        }
    }
}
