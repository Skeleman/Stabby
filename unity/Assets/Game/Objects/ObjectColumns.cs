using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Stabby.Viewer;
using Stabby.World;
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
            public BlobAssetReference<ObjectColumnData> Blob;
            public int Columns;
            public long Ms;
        }

        Task<Prepared> build;
        BlobAssetReference<ObjectColumnData> blob;
        ObjectLibrary lib;

        public ObjectField Field { get; private set; }
        public ObjectLibrary Library => lib;

        protected override void OnCreate()
        {
            var ids = new System.Collections.Generic.List<string>();
            foreach (var b in BiomeFile.Load(BiomeFile.DefaultPath)) ids.Add(b.Id);
            lib = ObjectFile.Load(ObjectFile.DefaultPath, ids);
        }

        protected override void OnDestroy()
        {
            if (blob.IsCreated) blob.Dispose();
            Owned.Release(build, p => p.Blob.Dispose());
        }

        static BlobAssetReference<ObjectColumnData> Pack(ColumnArrays cols)
        {
            int startsAt = 32, startsBytes = cols.Starts.Length * 4;
            int itemsAt = (startsAt + startsBytes + 15) & ~15;
            var itemBytes = MemoryMarshal.AsBytes(cols.Items.AsSpan());
            var bytes = new byte[itemsAt + itemBytes.Length];
            var span = bytes.AsSpan();
            MemoryMarshal.Write(span, ref cols.Cell);
            MemoryMarshal.Write(span.Slice(4), ref cols.Max);
            int so = startsAt - 8, sl = cols.Starts.Length, io = itemsAt - 16, il = cols.Items.Length;
            MemoryMarshal.Write(span.Slice(8), ref so);
            MemoryMarshal.Write(span.Slice(12), ref sl);
            MemoryMarshal.Write(span.Slice(16), ref io);
            MemoryMarshal.Write(span.Slice(20), ref il);
            MemoryMarshal.AsBytes(cols.Starts.AsSpan()).CopyTo(span.Slice(startsAt));
            itemBytes.CopyTo(span.Slice(itemsAt));
            return BlobAssetReference<ObjectColumnData>.Create(bytes);
        }

        (GeneratedWorld, GroundParts) MapReady()
        {
            if (World.IsServer())
            {
                var server = World.GetExistingSystemManaged<ServerMapSystem>();
                return (server.Map, server.Parts);
            }
            var client = World.GetExistingSystemManaged<ClientMapSystem>();
            return client.Map != null && client.Parts != null && SystemAPI.HasSingleton<Ground>() ? (client.Map, client.Parts) : default;
        }

        protected override void OnUpdate()
        {
            if (build == null)
            {
                var (w, parts) = MapReady();
                if (w == null || parts == null) return;
                var objects = lib;
                build = Task.Run(() =>
                {
                    var sw = Stopwatch.StartNew();
                    var field = new ObjectField(w, parts.Detail, parts.Edits, objects);
                    var cols = ColumnArrays.Build(field.Columns());
                    var made = Pack(cols);
                    return new Prepared { Field = field, Blob = made, Columns = cols.Items.Length, Ms = sw.ElapsedMilliseconds };
                });
                return;
            }
            if (!build.IsCompleted) return;
            Enabled = false;
            if (build.IsFaulted) { GameStatus.Failure = build.Exception.ToString(); build = null; return; }
            var p = build.Result;
            build = null;
            blob = p.Blob;
            EntityManager.CreateSingleton(new ObjectColumns { Blob = blob });
            Field = p.Field;
            GameStatus.Lines[World.Name + "~objects"] = $"{World.Name}: {p.Columns} object columns in {p.Ms} ms";
        }
    }
}
