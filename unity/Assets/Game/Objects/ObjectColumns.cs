using System.Diagnostics;
using System.Threading.Tasks;
using Stabby.Viewer;
using Stabby.World;
using Unity.Collections;
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
            public BlobBuilder Blob;
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

        GeneratedWorld MapReady()
        {
            if (World.IsServer()) return World.GetExistingSystemManaged<ServerMapSystem>().Map;
            var map = World.GetExistingSystemManaged<ClientMapSystem>().Map;
            return map != null && SystemAPI.HasSingleton<Ground>() ? map : null;
        }

        protected override void OnUpdate()
        {
            if (build == null)
            {
                var w = MapReady();
                if (w == null) return;
                var objects = lib;
                build = Task.Run(() =>
                {
                    var sw = Stopwatch.StartNew();
                    var detail = new TerrainDetail(w);
                    var field = new ObjectField(w, detail, Features.Place(w, detail), objects);
                    var cols = ColumnArrays.Build(field.Columns());
                    var b = new BlobBuilder(Allocator.Persistent);
                    ref var root = ref b.ConstructRoot<ObjectColumnData>();
                    root.Cell = cols.Cell;
                    root.Max = cols.Max;
                    var s = b.Allocate(ref root.Starts, cols.Starts.Length);
                    for (int k = 0; k < cols.Starts.Length; k++) s[k] = cols.Starts[k];
                    var items = b.Allocate(ref root.Items, cols.Items.Length);
                    for (int k = 0; k < cols.Items.Length; k++) items[k] = cols.Items[k];
                    return new Prepared { Field = field, Blob = b, Columns = cols.Items.Length, Ms = sw.ElapsedMilliseconds };
                });
                return;
            }
            if (!build.IsCompleted) return;
            Enabled = false;
            if (build.IsFaulted) { GameStatus.Failure = build.Exception.ToString(); build = null; return; }
            var p = build.Result;
            build = null;
            blob = p.Blob.CreateBlobAssetReference<ObjectColumnData>(Allocator.Persistent);
            p.Blob.Dispose();
            EntityManager.CreateSingleton(new ObjectColumns { Blob = blob });
            Field = p.Field;
            GameStatus.Lines[World.Name + "~objects"] = $"{World.Name}: {p.Columns} object columns in {p.Ms} ms";
        }
    }
}
