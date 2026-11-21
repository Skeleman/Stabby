using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading.Tasks;
using Stabby.Viewer;
using Stabby.World;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.NetCode;

namespace Stabby.Game
{
    public struct ObjectColumnData
    {
        public float Cell, Max;
        public BlobArray<int> Starts;
        public BlobArray<ObjectColumn> Items;
    }

    public struct ObjectColumns : IComponentData, IColumnTable
    {
        public BlobAssetReference<ObjectColumnData> Blob;

        public float CellM => Blob.Value.Cell;
        public float MaxR => Blob.Value.Max;
        public int Mask => Blob.IsCreated ? Blob.Value.Starts.Length - 2 : -1;
        public int Start(int bucket) => Blob.Value.Starts[bucket];
        public ObjectColumn Column(int k) => Blob.Value.Items[k];
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.ClientSimulation)]
    public partial class ObjectColumnsSystem : SystemBase
    {
        sealed class Prepared
        {
            public ObjectField Field;
            public BlobBuilder Builder;
            public bool Built;
            public int Columns;
            public long Ms;
            public bool Reused;
        }

        Task<Prepared> build;
        Prepared baking;
        BlobBake<ObjectColumnData> bake;
        BlobAssetReference<ObjectColumnData> blob;
        ObjectLibrary lib;

        public ObjectField Field { get; private set; }
        public ObjectLibrary Library => lib;

        protected override void OnCreate() => lib = ContentFiles.Objects(ContentFiles.Biomes());

        protected override void OnDestroy()
        {
            if (blob.IsCreated) blob.Dispose();
            bake.Release();
            Owned.Release(build, p => { if (p.Built) p.Builder.Dispose(); });
        }

        static WorldStore.SharedObjects Sort(ObjectField field)
        {
            var rows = new UnsafeList<ObjectColumn>[field.ChunksAcross];
            for (int j = 0; j < rows.Length; j++) rows[j] = new UnsafeList<ObjectColumn>(0, Allocator.Persistent);
            try
            {
                field.ColumnRows((cz, columns) =>
                {
                    ref var row = ref rows[cz];
                    if (row.Capacity < row.Length + columns.Count) row.SetCapacity(Math.Max(row.Length + columns.Count, row.Capacity * 2));
                    foreach (var c in columns) row.AddNoResize(c);
                });
                int count = 0;
                foreach (var r in rows) count += r.Length;
                int buckets = ColumnArrays.Buckets(count);
                const float cell = ColumnArrays.DefaultCellM;
                var starts = new NativeArray<int>(buckets + 1, Allocator.Persistent);
                var items = new NativeArray<ObjectColumn>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                var at = new NativeArray<int>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                var fill = new NativeArray<int>(buckets, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                float max = 0;
                int k = 0;
                foreach (var r in rows)
                    for (int q = 0; q < r.Length; q++)
                    {
                        var c = r[q];
                        max = Math.Max(max, c.R);
                        at[k] = Obstacles.Bucket((int)Math.Floor(c.X / cell), (int)Math.Floor(c.Z / cell), buckets - 1);
                        starts[at[k++] + 1]++;
                    }
                for (int b = 0; b < buckets; b++) { starts[b + 1] += starts[b]; fill[b] = starts[b]; }
                k = 0;
                foreach (var r in rows)
                    for (int q = 0; q < r.Length; q++) items[fill[at[k++]]++] = r[q];
                at.Dispose();
                fill.Dispose();
                return new WorldStore.SharedObjects { Field = field, Starts = starts, Items = items, Cell = cell, Max = max };
            }
            finally
            {
                for (int j = 0; j < rows.Length; j++) rows[j].Dispose();
            }
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
            if (build == null && baking == null)
            {
                var (w, parts, entry) = MapReady();
                if (w == null || parts == null) return;
                var objects = lib;
                bool ran = false;
                Func<WorldStore.SharedObjects> make = () =>
                {
                    ran = true;
                    var sw = Stopwatch.StartNew();
                    var field = new ObjectField(w, parts.Detail, parts.Edits, objects);
                    var shared = Sort(field);
                    shared.Ms = sw.ElapsedMilliseconds;
                    return shared;
                };
                bool own = entry == null;
                var source = own ? Task.Run(make) : WorldStore.Objects(entry, make);
                build = Task.Run(async () =>
                {
                    var s = await source;
                    bool built = s.Build(out var b);
                    int count = s.Count;
                    if (own) s.Dispose();
                    return new Prepared { Field = s.Field, Builder = b, Built = built, Columns = count, Ms = s.Ms, Reused = !ran };
                });
                return;
            }
            if (baking == null)
            {
                if (!build.IsCompleted) return;
                if (build.IsFaulted) { GameStatus.Failure = build.Exception.ToString(); build = null; Enabled = false; return; }
                baking = build.Result;
                build = null;
                if (!baking.Built) { GameStatus.Failure = $"{World.Name}: shared object columns were released"; Enabled = false; return; }
                bake = BlobBake<ObjectColumnData>.Start(baking.Builder);
                return;
            }
            if (!bake.IsCompleted) return;
            Enabled = false;
            var p = baking;
            baking = null;
            blob = bake.Take();
            EntityManager.CreateSingleton(new ObjectColumns { Blob = blob });
            Field = p.Field;
            GameStatus.Lines[World.Name + "~objects"] = $"{World.Name}: {p.Columns} object columns in {p.Ms} ms" + (p.Reused ? ", shared" : "");
        }
    }
}
