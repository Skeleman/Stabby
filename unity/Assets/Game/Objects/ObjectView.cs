using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Stabby.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace Stabby.Game
{
    [MaterialProperty("_BaseColor")]
    public struct ObjectColor : IComponentData { public float4 Value; }

    [MaterialProperty("_Fade")]
    public struct ObjectFade : IComponentData { public float4 Value; }

    [MaterialProperty("_Leaf")]
    public struct ObjectLeaf : IComponentData { public float4 Value; }

    [MaterialProperty("_Imp")]
    public struct ObjectImp : IComponentData { public float Value; }

    public struct ObjectLod : IComponentData
    {
        public BatchMeshID L0, L1, L2, Imp, Blend;
        public Detail.Rule Rule;
        public Detail.Decision Decision;
        public float Radius, Impostor;
    }

    public struct ObjectSpawn
    {
        public ulong Id;
        public float3 P;
        public quaternion Q;
        public float Size, Contact, Ground, Field;
        public ushort Def, Variant;
    }

    public struct ObjectDefPart
    {
        public float3 Offset, Scale;
        public int Mesh, Ground;
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class ObjectViewSystem : SystemBase
    {
        public const int Lods = 3, Stride = Lods + 2, SpawnBudget = 8000, DespawnBudget = 8000, Frames = 8, Cell = 64;
        const int GroundContact = 0, GroundDungeon = 1, GroundField = 2;
        public const float DespawnReach = 1f / (1f - Detail.Hysteresis), UnloadReach = 1.5f;

        struct MeshSpan
        {
            public int Vertex, VertexCount, Index, IndexCount, Uvs;
            public bool Wide;
        }

        sealed class MeshSet
        {
            public NativeList<float> Vertices;
            public NativeList<ushort> Narrow;
            public NativeList<int> Wide;
            public MeshSpan[] Spans;
            public AABB[] Bounds;
            public Vector4[] Centers;
            public int[] Slots;
            public int SlotCount;
            public MeshPack Pack;
            public readonly ConcurrentQueue<MeshPack> Packs = new ConcurrentQueue<MeshPack>();

            public void Fresh()
            {
                Vertices = new NativeList<float>(1 << 18, Allocator.Persistent);
                Narrow = new NativeList<ushort>(1 << 16, Allocator.Persistent);
                Wide = new NativeList<int>(0, Allocator.Persistent);
            }

            public void DisposeLists()
            {
                if (Vertices.IsCreated) Vertices.Dispose();
                if (Narrow.IsCreated) Narrow.Dispose();
                if (Wide.IsCreated) Wide.Dispose();
                Vertices = default; Narrow = default; Wide = default;
            }

            public void Dispose()
            {
                Pack?.Dispose();
                Pack = null;
                while (Packs.TryDequeue(out var p)) p.Dispose();
                DisposeLists();
            }
        }

        struct ChunkParts
        {
            public int Chunk;
            public float Base, Ms;
            public NativeArray<long> Keys;
            public NativeArray<int3> Ranges;
            public NativeArray<ObjectSpawn> Objects;
            public NativeArray<uint> Parts;
            public List<LightEmitter> Lights;

            public void Dispose()
            {
                Keys.Dispose();
                Ranges.Dispose();
                Objects.Dispose();
                Parts.Dispose();
            }
        }

        ObjectField field;
        ObjectLibrary lib;
        float cm, worldM, span;
        int across;
        int[][] meshBase;
        int[] partBase;
        NativeArray<ObjectDefPart> defPartsNative;
        ObjectDefPart[] defPartTable;
        float[] reach, baseReach, tile;
        float reachQuality;
        Task<MeshSet> meshTask;
        MeshSet meshSet;
        Mesh[] meshes;
        BatchMeshID[] meshIds;
        MeshBatch meshBatch;
        bool meshesDone;
        readonly List<Mesh> pumped = new List<Mesh>();
        readonly List<int> pumpedIds = new List<int>();
        public const int FoliageLayer = 8;
        Material material, foliageMaterial;
        BatchMaterialID materialId, foliageMaterialId;
        Entity castPrefab, flatPrefab, castFoliagePrefab, flatFoliagePrefab;
        bool[] foliageMesh, shadowMesh;
        NativeArray<float4> colorNative, leafNative;
        NativeArray<bool> shadowNative;
        Task<LeafAtlas.Pixels> atlasTask;
        Texture2D atlas;
        RenderTexture impA, impB;
        readonly List<Task<ChunkParts>> chunkTasks = new List<Task<ChunkParts>>();
        bool[] collectingChunk;
        float[] heldBase, blockedSince;
        int[] blockedFrame;
        float3 lastPos;
        bool settled;
        int recollects;
        float firstMaxMs, recollectMaxMs, partsWaitMax;
        static readonly int Collectors = math.max(1, Environment.ProcessorCount / 4);
        NativeArray<DefLod> defLods;
        NativeArray<BatchMeshID> meshIdsNative;
        NativeArray<AABB> boundsNative;
        NativeArray<int> slotsNative;
        NativeList<SpawnWrite> writes;
        readonly int[] groupCounts = new int[4], groupAt = new int[4];
        readonly NativeArray<Entity>[] groupMade = new NativeArray<Entity>[4];
        readonly Entity[] groupPrefabs = new Entity[4];
        readonly List<long> pickKeys = new List<long>();
        readonly List<int3> pickRanges = new List<int3>();
        NativeArray<bool> loaded;
        NativeArray<ObjectSpawn>[] chunkObjects;
        NativeArray<uint>[] chunkParts;
        NativeArray<long>[] chunkKeys;
        List<Entity>[] chunkLights;
        NativeParallelHashMap<long, int3> ranges;
        NativeParallelHashSet<long> spawned;
        NativeParallelMultiHashMap<long, Entity> tileEntities;
        NativeArray<float> tileNative, reachNative;
        NativeList<long> doomedKeys, wantKeys;
        JobHandle scan;
        bool scanned;
        float3 scanAt;
        int liveEntities, chunksLoaded, chunksHeld, chunksUnloaded, partsHeld, objectsHeld, peakParts, peakChunks;
        int[] liveByDef;
        ObjectKind[] kinds;
        Detail.Rule[] rules;
        float worstUpdate;
        static readonly Unity.Profiling.ProfilerMarker LoadMarker = new Unity.Profiling.ProfilerMarker("ObjectView.LoadChunks"), DespawnMarker = new Unity.Profiling.ProfilerMarker("ObjectView.Despawn"), SpawnMarker = new Unity.Profiling.ProfilerMarker("ObjectView.Spawn"), RegisterMarker = new Unity.Profiling.ProfilerMarker("ObjectView.Register");

        public static int Pending { get; private set; } = 1;
        public static int PendingWhy { get; private set; }
        int why;
        static float3 readyAt;
        static int readyFrame = -1;
        public static bool ReadyAround(float3 p, float within) => readyFrame >= UnityEngine.Time.frameCount - 2 && math.distance(readyAt, p) <= within;
        public static int LiveEntities { get; private set; }
        public static int PartsHeld { get; private set; }
        public static int ObjectsHeld { get; private set; }
        public static long PartBytes => (long)PartsHeld * sizeof(uint) + (long)ObjectsHeld * Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf<ObjectSpawn>();
        public static int Recollects { get; private set; }
        public static float FirstCollectMaxMs { get; private set; }
        public static float RecollectMaxMs { get; private set; }
        public static float PartsWaitMaxMs { get; private set; }
        public static int ChunksHeld { get; private set; }
        public static int ChunksUnloaded { get; private set; }
        public static float LastMs { get; private set; }
        static int[] liveByDefShared;
        static ObjectLibrary libShared;

        public static string PerDef()
        {
            if (libShared == null || liveByDefShared == null) return "";
            var sb = new System.Text.StringBuilder();
            for (int d = 0; d < liveByDefShared.Length; d++) sb.Append(d == 0 ? "" : ", ").Append(libShared.Objects[d].Id).Append(' ').Append(liveByDefShared[d]);
            return sb.ToString();
        }

        protected override void OnCreate()
        {
            if (World.Name != GameSettings.ViewClient) { Enabled = false; return; }
            Pending = 1;
            readyFrame = -1;
            ranges = new NativeParallelHashMap<long, int3>(1024, Allocator.Persistent);
            spawned = new NativeParallelHashSet<long>(1024, Allocator.Persistent);
            tileEntities = new NativeParallelMultiHashMap<long, Entity>(4096, Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            if (GameStatus.Live.Remove("~objects view")) GameStatus.Lines["~objects view"] = Status();
            if (chunkParts != null) foreach (var a in chunkParts) if (a.IsCreated) a.Dispose();
            if (chunkObjects != null) foreach (var a in chunkObjects) if (a.IsCreated) a.Dispose();
            if (chunkKeys != null) foreach (var a in chunkKeys) if (a.IsCreated) a.Dispose();
            if (chunkLights != null) foreach (var a in chunkLights) DestroyLights(a);
            foreach (var t in chunkTasks) Owned.Release(t, r => r.Dispose());
            meshBatch?.Dispose();
            if (meshSet != null) { meshSet.Pack?.Dispose(); meshSet.Pack = null; }
            if (meshTask != null) Owned.Release(meshTask, r => r.Dispose());
            else meshSet?.Dispose();
            EntityManager.CompleteAllTrackedJobs();
            JobPoll.Teardown(scan);
            if (tileNative.IsCreated) tileNative.Dispose();
            if (reachNative.IsCreated) reachNative.Dispose();
            if (doomedKeys.IsCreated) doomedKeys.Dispose();
            if (wantKeys.IsCreated) wantKeys.Dispose();
            if (defLods.IsCreated) defLods.Dispose();
            if (meshIdsNative.IsCreated) meshIdsNative.Dispose();
            if (boundsNative.IsCreated) boundsNative.Dispose();
            if (slotsNative.IsCreated) slotsNative.Dispose();
            if (colorNative.IsCreated) colorNative.Dispose();
            if (leafNative.IsCreated) leafNative.Dispose();
            if (shadowNative.IsCreated) shadowNative.Dispose();
            if (defPartsNative.IsCreated) defPartsNative.Dispose();
            if (ranges.IsCreated) ranges.Dispose();
            if (spawned.IsCreated) spawned.Dispose();
            if (tileEntities.IsCreated) tileEntities.Dispose();
            DisposeHidden();
            if (loaded.IsCreated) loaded.Dispose();
            if (meshes != null) foreach (var m in meshes) if (m != null) UnityEngine.Object.Destroy(m);
            if (material != null) UnityEngine.Object.Destroy(material);
            if (foliageMaterial != null) UnityEngine.Object.Destroy(foliageMaterial);
            if (atlas != null) UnityEngine.Object.Destroy(atlas);
            if (impA != null) { impA.Release(); UnityEngine.Object.Destroy(impA); }
            if (impB != null) { impB.Release(); UnityEngine.Object.Destroy(impB); }
        }

        public const float GlowBase = 0.05f, GlowSpan = 0.2f, GlowPatternStep = 0.22f, IceBase = 0.48f, IceSpan = 0.015f, OwnStone = 0.0125f, OwnStoneKeepHue = 0.019f, OwnStoneRust = 0.022f;

        static float4 ColorBase(ObjectPart part)
        {
            if (part.Translucency > 0f) return new float4(Decode(part.Color), IceBase + IceSpan * part.Translucency);
            if (part.Glow > 0f) return new float4(Decode(part.Color), GlowBase + GlowSpan * part.Glow + GlowPatternStep * part.GlowPattern);
            if (ObjectPart.IsStone(part.Shape)) return part.HasColor ? new float4(Decode(part.Color), part.Rust ? OwnStoneRust : part.KeepHue ? OwnStoneKeepHue : OwnStone) : new float4(1, 1, 1, 0);
            return new float4(Decode(part.Color), part.Shape);
        }

        static float4 LeafBase(ObjectPart part) => ObjectPart.IsFoliage(part.Shape) ? new float4(Decode(part.LeafColor), 2) : float4.zero;

        static float3 Decode(uint rgb)
        {
            var c = new float3((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255) / 255f;
            return math.select(math.pow((c + 0.055f) / 1.055f, 2.4f), c / 12.92f, c <= 0.04045f);
        }

        bool Prepare()
        {
            var cols = World.GetExistingSystemManaged<ObjectColumnsSystem>();
            var map = World.GetExistingSystemManaged<ClientMapSystem>()?.Map;
            if (cols?.Field == null || map == null) return false;
            field = cols.Field;
            lib = field.Library;
            cm = map.Config.CellMeters;
            worldM = map.Config.Size * cm;
            span = WorldChunks.Size * cm;
            across = field.ChunksAcross;
            loaded = new NativeArray<bool>(across * across, Allocator.Persistent);
            chunkObjects = new NativeArray<ObjectSpawn>[across * across];
            chunkParts = new NativeArray<uint>[across * across];
            chunkKeys = new NativeArray<long>[across * across];
            chunkLights = new List<Entity>[across * across];
            collectingChunk = new bool[across * across];
            heldBase = new float[across * across];
            blockedSince = new float[across * across];
            blockedFrame = new int[across * across];
            for (int i = 0; i < blockedSince.Length; i++) blockedSince[i] = -1f;
            float minTile = cm * 0.5f;
            int defs = lib.Objects.Count, count = 0;
            meshBase = new int[defs][];
            partBase = new int[defs];
            var defParts = new List<ObjectDefPart>();
            reach = new float[defs];
            baseReach = new float[defs];
            tile = new float[defs];
            liveByDef = new int[defs];
            kinds = new ObjectKind[defs];
            rules = new Detail.Rule[defs];
            var shapes = new List<int>();
            var keys = new List<ulong>();
            var parts = new List<ObjectPart>();
            var impostors = new List<bool>();
            var colorBase = new List<float4>();
            var leafBase = new List<float4>();
            var shadows = new List<bool>();
            for (int d = 0; d < defs; d++)
            {
                var def = lib.Objects[d];
                var kind = lib.KindOf(def);
                meshBase[d] = new int[def.Parts.Count];
                partBase[d] = defParts.Count;
                for (int p = 0; p < def.Parts.Count; p++)
                {
                    meshBase[d][p] = count;
                    var dp = def.Parts[p];
                    defParts.Add(new ObjectDefPart
                    {
                        Offset = new float3(dp.OffsetX, dp.OffsetY, dp.OffsetZ), Scale = new float3(dp.ScaleX, dp.ScaleY, dp.ScaleZ), Mesh = count,
                        Ground = dp.Shape == ObjectPart.Dungeon ? GroundDungeon : ObjectPart.IsStructure(dp.Shape) && !ObjectField.IsCave(def) ? GroundField : GroundContact,
                    });
                    for (int v = 0; v < def.Variants; v++)
                    {
                        shapes.Add(def.Parts[p].Shape); parts.Add(def.Parts[p]); keys.Add(ObjectMeshes.Key(def.Id, p, v));
                        impostors.Add(kind.ImpostorPixels > 0 && ObjectPart.IsFoliage(def.Parts[p].Shape));
                        colorBase.Add(ColorBase(def.Parts[p])); leafBase.Add(LeafBase(def.Parts[p])); shadows.Add(lib.PartShadows(def, def.Parts[p]));
                        count++;
                    }
                }
                kinds[d] = kind;
                rules[d] = kind.Rule();
                baseReach[d] = Detail.SimulationReach(2f * field.Footprint(d) * def.Placement.SizeMaxM, kind.FadePixels);
                reach[d] = baseReach[d] * GameSettings.GraphicsScale;
                float t = span;
                while (t > reach[d] / 3f && t * 0.5f >= minTile) t *= 0.5f;
                tile[d] = t;
            }
            tileNative = new NativeArray<float>(tile, Allocator.Persistent);
            reachNative = new NativeArray<float>(reach, Allocator.Persistent);
            doomedKeys = new NativeList<long>(256, Allocator.Persistent);
            wantKeys = new NativeList<long>(256, Allocator.Persistent);
            foliageMesh = new bool[shapes.Count];
            shadowMesh = shadows.ToArray();
            colorNative = new NativeArray<float4>(colorBase.ToArray(), Allocator.Persistent);
            leafNative = new NativeArray<float4>(leafBase.ToArray(), Allocator.Persistent);
            shadowNative = new NativeArray<bool>(shadowMesh, Allocator.Persistent);
            defPartTable = defParts.ToArray();
            defPartsNative = new NativeArray<ObjectDefPart>(defPartTable, Allocator.Persistent);
            bool anyFoliage = false;
            for (int k = 0; k < shapes.Count; k++) anyFoliage |= foliageMesh[k] = ObjectPart.IsFoliage(shapes[k]);
            int spans = shapes.Count * Stride;
            meshSet = new MeshSet { Spans = new MeshSpan[spans], Bounds = new AABB[shapes.Count], Centers = new Vector4[shapes.Count], Slots = new int[shapes.Count] };
            meshes = new Mesh[spans];
            meshIds = new BatchMeshID[spans];
            var set = meshSet;
            meshTask = Task.Run(() => BuildMeshes(set, shapes, parts, keys, impostors));
            if (anyFoliage) atlasTask = Task.Run(() => LeafAtlas.Build());
            return true;
        }

        static readonly VertexAttributeDescriptor[][] Layouts =
        {
            new[] { new VertexAttributeDescriptor(VertexAttribute.Position), new VertexAttributeDescriptor(VertexAttribute.Normal) },
            new[] { new VertexAttributeDescriptor(VertexAttribute.Position), new VertexAttributeDescriptor(VertexAttribute.Normal), new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4) },
            new[]
            {
                new VertexAttributeDescriptor(VertexAttribute.Position), new VertexAttributeDescriptor(VertexAttribute.Normal), new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
            },
        };
        static readonly int[] Quad = { 0, 1, 2, 0, 2, 3 };

        static int Floats(int uvs) => 6 + 4 * uvs;

        static Task<ObjectMesh[]> BuildBatch(List<ObjectPart> parts, List<ulong> keys, int b0, int bn) => Task.Run(() =>
        {
            var built = new ObjectMesh[bn * Lods];
            Parallel.For(0, bn * Lods, j => built[j] = ObjectMeshes.Build(parts[b0 + j / Lods], keys[b0 + j / Lods], j % Lods));
            return built;
        });

        static async Task<MeshSet> BuildMeshes(MeshSet set, List<int> shapes, List<ObjectPart> parts, List<ulong> keys, List<bool> impostors)
        {
            const int batch = 128, ahead = 1;
            int count = shapes.Count, batches = (count + batch - 1) / batch;
            var pending = new Task<ObjectMesh[]>[batches];
            for (int b = 0; b < Math.Min(ahead, batches); b++) pending[b] = BuildBatch(parts, keys, b * batch, Math.Min(batch, count - b * batch));
            try
            {
                for (int i = 0; i < set.Spans.Length; i++) set.Spans[i].Uvs = -1;
                for (int b = 0; b < batches; b++)
                {
                    var built = await pending[b].ConfigureAwait(false);
                    pending[b] = null;
                    int b0 = b * batch, bn = Math.Min(batch, count - b0);
                    set.Fresh();
                    for (int k = b0; k < b0 + bn; k++)
                    {
                        for (int l = 0; l < Lods; l++)
                        {
                            var m = built[(k - b0) * Lods + l];
                            int vc = m.VertexCount, at = k * Stride + l;
                            float[] p = m.Positions, q = m.Normals;
                            Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity, mid = Vector3.zero;
                            for (int v = 0; v < vc; v++)
                            {
                                var pv = new Vector3(p[3 * v], p[3 * v + 1], p[3 * v + 2]);
                                lo = Vector3.Min(lo, pv); hi = Vector3.Max(hi, pv); mid += pv;
                            }
                            mid /= Mathf.Max(1, vc);
                            float outward = 0;
                            for (int v = 0; v < vc; v++) outward += Vector3.Dot(new Vector3(q[3 * v], q[3 * v + 1], q[3 * v + 2]), new Vector3(p[3 * v], p[3 * v + 1], p[3 * v + 2]) - mid);
                            var idx = m.Indices;
                            int uvs = m.Uvs != null ? 2 : m.Tones != null ? 1 : 0;
                            bool flip = m.Uvs == null && outward < 0;
                            if (flip) for (int t = 0; t < idx.Length; t += 3) (idx[t + 1], idx[t + 2]) = (idx[t + 2], idx[t + 1]);
                            int f = Floats(uvs), start = set.Vertices.Length;
                            set.Vertices.ResizeUninitialized(start + vc * f);
                            var dst = set.Vertices.AsArray();
                            for (int v = 0; v < vc; v++)
                            {
                                int o = start + v * f;
                                for (int c = 0; c < 3; c++)
                                {
                                    dst[o + c] = p[3 * v + c];
                                    dst[o + 3 + c] = flip ? -q[3 * v + c] : q[3 * v + c];
                                }
                                if (uvs == 2)
                                {
                                    for (int c = 0; c < 4; c++) { dst[o + 6 + c] = m.Uvs[4 * v + c]; dst[o + 10 + c] = m.Winds[4 * v + c]; }
                                }
                                else if (uvs == 1)
                                {
                                    for (int c = 0; c < 4; c++) dst[o + 6 + c] = m.Tones[4 * v + c];
                                }
                            }
                            Begin(set, at, start, vc, uvs);
                            foreach (var i in idx) Push(set, at, i);
                            End(set, at);
                            if (l == 0) set.Bounds[k] = new AABB { Center = (lo + hi) * 0.5f, Extents = (hi - lo) * (m.Uvs != null ? 0.56f : 0.5f) };
                        }
                        set.Slots[k] = -1;
                        if (impostors[k] && set.Spans[k * Stride].Uvs > 0) Impostor(set, k);
                    }
                    set.Packs.Enqueue(Pack(set, b0 * Stride, (b0 + bn) * Stride));
                    if (b + ahead < batches) pending[b + ahead] = BuildBatch(parts, keys, (b + ahead) * batch, Math.Min(batch, count - (b + ahead) * batch));
                }
            }
            catch
            {
                set.DisposeLists();
                while (set.Packs.TryDequeue(out var p)) p.Dispose();
                throw;
            }
            return set;
        }

        static void Begin(MeshSet set, int at, int start, int vc, int uvs)
        {
            ref var s = ref set.Spans[at];
            s.Vertex = start; s.VertexCount = vc; s.Uvs = uvs; s.Wide = vc > 65535;
            s.Index = s.Wide ? set.Wide.Length : set.Narrow.Length;
        }

        static void Push(MeshSet set, int at, int i)
        {
            if (set.Spans[at].Wide) set.Wide.Add(i);
            else set.Narrow.Add((ushort)i);
        }

        static void End(MeshSet set, int at)
        {
            ref var s = ref set.Spans[at];
            s.IndexCount = (s.Wide ? set.Wide.Length : set.Narrow.Length) - s.Index;
        }

        static Vector3 Position(NativeArray<float> src, int o) => new Vector3(src[o], src[o + 1], src[o + 2]);

        static void Impostor(MeshSet set, int k)
        {
            var l0 = set.Spans[k * Stride];
            var src = set.Vertices.AsArray();
            int f0 = Floats(l0.Uvs);
            Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
            for (int v = 0; v < l0.VertexCount; v++) { var p = Position(src, l0.Vertex + v * f0); lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
            var c = (lo + hi) * 0.5f;
            float r = 0;
            for (int v = 0; v < l0.VertexCount; v++) { var p = Position(src, l0.Vertex + v * f0); r = Mathf.Max(r, new Vector2(p.x - c.x, p.z - c.z).magnitude); }
            float s = Mathf.Max(2f * r, hi.y - lo.y) * 1.02f;
            int slot = set.SlotCount++;
            set.Slots[k] = slot;
            var center = new Vector4(c.x, c.y, c.z, s);
            set.Centers[k] = center;
            var b = set.Bounds[k];
            b.Extents = math.max(b.Extents, new float3(0.5f * s, 0.5f * s, 0.5f * s));
            b.Center = c;
            set.Bounds[k] = b;
            int f = Floats(2), at = k * Stride + Lods, quad = set.Vertices.Length;
            set.Vertices.ResizeUninitialized(quad + 4 * f);
            var dst = set.Vertices.AsArray();
            for (int v = 0; v < 4; v++)
            {
                float x = v == 0 || v == 3 ? -1 : 1, y = v < 2 ? -1 : 1;
                var qp = c + new Vector3(x * 0.5f * s, y * 0.5f * s, 0);
                int o = quad + v * f;
                dst[o] = qp.x; dst[o + 1] = qp.y; dst[o + 2] = qp.z;
                dst[o + 3] = 0; dst[o + 4] = 0; dst[o + 5] = 1;
                dst[o + 6] = x; dst[o + 7] = y; dst[o + 8] = slot; dst[o + 9] = 2;
                for (int e = 0; e < 4; e++) dst[o + 10 + e] = center[e];
            }
            Begin(set, at, quad, 4, 2);
            foreach (var i in Quad) Push(set, at, i);
            End(set, at);
            var l2 = set.Spans[k * Stride + Lods - 1];
            if (l2.Uvs != 2) throw new InvalidOperationException("impostor blend needs both uv sets at the last lod");
            int blend = set.Vertices.Length, vc = l2.VertexCount;
            set.Vertices.ResizeUninitialized(blend + (vc + 4) * f);
            dst = set.Vertices.AsArray();
            NativeArray<float>.Copy(dst, l2.Vertex, dst, blend, vc * f);
            NativeArray<float>.Copy(dst, quad, dst, blend + vc * f, 4 * f);
            Begin(set, at + 1, blend, vc + 4, 2);
            for (int i = 0; i < l2.IndexCount; i++) Push(set, at + 1, l2.Wide ? set.Wide[l2.Index + i] : set.Narrow[l2.Index + i]);
            foreach (var i in Quad) Push(set, at + 1, vc + i);
            End(set, at + 1);
        }

        static MeshPack Pack(MeshSet set, int from, int to)
        {
            var pack = new MeshPack(set.Vertices, set.Narrow, set.Wide);
            set.Vertices = default; set.Narrow = default; set.Wide = default;
            var attr = new int[Layouts.Length];
            for (int u = 0; u < Layouts.Length; u++) attr[u] = pack.Layout(Layouts[u]);
            for (int i = from; i < to; i++)
            {
                var s = set.Spans[i];
                if (s.Uvs < 0) continue;
                pack.Add("Object " + i, new MeshPiece
                {
                    Id = i, Vertex = s.Vertex, VertexCount = s.VertexCount, Floats = Floats(s.Uvs), Index = s.Index, IndexCount = s.IndexCount, Attr = attr[s.Uvs], AttrCount = Layouts[s.Uvs].Length, Wide = s.Wide,
                });
            }
            return pack;
        }

        void Bake(CommandBuffer cmd)
        {
            int size = Cell;
            while ((size / Cell) * (size / Cell) < meshSet.SlotCount * Frames) size *= 2;
            int cols = size / Cell;
            float fill = (Cell - 4f) / Cell;
            impA = Target(size, "Stabby impostor albedo");
            impB = Target(size, "Stabby impostor normal");
            var depth = new RenderTexture(size, size, 24, RenderTextureFormat.Depth) { name = "Stabby impostor depth" };
            depth.Create();
            cmd.SetRenderTarget(new RenderTargetIdentifier[] { impA, impB }, depth.depthBuffer);
            cmd.ClearRenderTarget(true, true, Color.clear);
            cmd.SetGlobalVector("_StabbyBakeGrid", new Vector4(cols, fill, 0, 0));
            int pass = foliageMaterial.FindPass("Bake");
            for (int k = 0; k < meshSet.Slots.Length; k++)
            {
                if (meshSet.Slots[k] < 0) continue;
                cmd.SetGlobalVector("_StabbyBakeCenter", meshSet.Centers[k]);
                for (int f = 0; f < Frames; f++)
                {
                    float a = f * 2f * Mathf.PI / Frames;
                    int cell = meshSet.Slots[k] * Frames + f;
                    cmd.SetGlobalVector("_StabbyBakeFrame", new Vector4(Mathf.Sin(a), Mathf.Cos(a), cell % cols, cell / cols));
                    cmd.DrawMesh(meshes[k * Stride], Matrix4x4.identity, foliageMaterial, 0, pass);
                }
            }
            cmd.GenerateMips(impA);
            cmd.GenerateMips(impB);
            Graphics.ExecuteCommandBuffer(cmd);
            depth.Release();
            UnityEngine.Object.Destroy(depth);
            foliageMaterial.SetTexture("_ImpA", impA);
            foliageMaterial.SetTexture("_ImpB", impB);
            foliageMaterial.SetVector("_ImpGrid", new Vector4(cols, fill, Frames, 0));
        }

        static RenderTexture Target(int size, string name)
        {
            var rt = new RenderTexture(new RenderTextureDescriptor(size, size, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, 0) { useMipMap = true, autoGenerateMips = false })
            {
                name = name, filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp,
            };
            rt.Create();
            return rt;
        }

        bool Register()
        {
            if (meshesDone) return true;
            var graphics = World.GetExistingSystemManaged<EntitiesGraphicsSystem>();
            while (true)
            {
                if (meshBatch == null)
                {
                    if (!meshSet.Packs.TryDequeue(out var pack))
                    {
                        if (meshTask == null) break;
                        if (!meshTask.IsCompleted) { PendingWhy = 512; return false; }
                        if (meshTask.IsFaulted) { GameStatus.Failure = meshTask.Exception.ToString(); meshTask = null; return false; }
                        meshTask = null;
                        continue;
                    }
                    meshSet.Pack = pack;
                    meshBatch = new MeshBatch(pack);
                }
                if (meshBatch.Pump(pumped, pumpedIds))
                    for (int i = 0; i < pumped.Count; i++)
                    {
                        meshes[pumpedIds[i]] = pumped[i];
                        meshIds[pumpedIds[i]] = graphics.RegisterMesh(pumped[i]);
                    }
                if (!meshBatch.Done) { PendingWhy = 2048; return false; }
                meshBatch.Dispose();
                meshBatch = null;
                meshSet.Pack.Dispose();
                meshSet.Pack = null;
            }
            if (atlasTask != null)
            {
                if (!atlasTask.IsCompleted) { PendingWhy = 1024; return false; }
                if (atlasTask.IsFaulted) { GameStatus.Failure = atlasTask.Exception.ToString(); atlasTask = null; return false; }
                atlas = LeafAtlas.Create(atlasTask.Result);
                atlasTask = null;
            }
            meshesDone = true;
            meshSet.Dispose();
            meshIdsNative = new NativeArray<BatchMeshID>(meshIds, Allocator.Persistent);
            boundsNative = new NativeArray<AABB>(meshSet.Bounds, Allocator.Persistent);
            slotsNative = new NativeArray<int>(meshSet.Slots, Allocator.Persistent);
            defLods = new NativeArray<DefLod>(kinds.Length, Allocator.Persistent);
            for (int d = 0; d < kinds.Length; d++)
            {
                var kind = kinds[d];
                defLods[d] = new DefLod { Lods = math.clamp(kind.Lods, 1, Lods), Rule = rules[d], Impostor = kind.ImpostorPixels };
            }
            castPrefab = Prefab(ShadowCastingMode.On, materialId, false);
            flatPrefab = Prefab(ShadowCastingMode.Off, materialId, false);
            if (atlas != null)
            {
                foliageMaterial = new Material(Shader.Find("Stabby/Foliage")) { name = "Stabby foliage", enableInstancing = true };
                foliageMaterial.SetTexture("_LeafAtlas", atlas);
                if (meshSet.SlotCount > 0)
                {
                    using var cmd = new CommandBuffer { name = "Stabby impostor bake" };
                    Bake(cmd);
                }
                foliageMaterialId = graphics.RegisterMaterial(foliageMaterial);
                castFoliagePrefab = Prefab(ShadowCastingMode.On, foliageMaterialId, true);
                flatFoliagePrefab = Prefab(ShadowCastingMode.Off, foliageMaterialId, true);
            }
            return true;
        }

        Entity Prefab(ShadowCastingMode shadows, BatchMaterialID mat, bool foliage)
        {
            var e = EntityManager.CreateEntity();
            RenderMeshUtility.AddComponents(e, EntityManager, new RenderMeshDescription(shadows, true, MotionVectorGenerationMode.ForceNoMotion, foliage ? FoliageLayer : 0), new MaterialMeshInfo(mat, meshIds[0]));
            if (foliage) EntityManager.AddComponentData(e, new ObjectLeaf { Value = new float4(0.2f, 0.3f, 0.1f, 2) });
            if (foliage) EntityManager.AddComponentData(e, new ObjectImp());
            EntityManager.AddComponentData(e, new ObjectColor { Value = new float4(1, 1, 1, 0) });
            EntityManager.AddComponentData(e, new ObjectFade { Value = new float4(1, 1, 0, 0) });
            EntityManager.AddComponentData(e, new ObjectLod());
            EntityManager.AddComponent<Prefab>(e);
            return e;
        }

        static long Key(int def, int tx, int tz) => ((long)def << 40) | ((long)tx << 20) | (long)tz;

        [System.ThreadStatic] static List<StructureLight> anchorScratch;

        ChunkParts Collect(int c, float minBase, bool lights)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var placed = ListPool.Rent<ObjectInstance>();
            try
            {
                var r = Collect(c, placed, minBase, lights);
                r.Ms = Since(t0);
                return r;
            }
            finally { ListPool.Return(placed); }
        }

        ChunkParts Collect(int c, List<ObjectInstance> placed, float minBase, bool lights)
        {
            int cx = c % across, cz = c / across;
            field.PlaceInto(cx, cz, placed);
            var emitters = lights ? LightsIn(placed) : null;
            int kept = 0;
            for (int i = 0; i < placed.Count; i++) if (baseReach[placed[i].Def] >= minBase) placed[kept++] = placed[i];
            placed.RemoveRange(kept, placed.Count - kept);
            int n = 0;
            for (int i = 0; i < placed.Count; i++) n += lib.Objects[placed[i].Def].Parts.Count;
            var order = new NativeArray<SortKey>(n, Allocator.Persistent);
            var objects = new NativeArray<ObjectSpawn>(placed.Count, Allocator.Persistent);
            var parts = new NativeArray<uint>(n, Allocator.Persistent);
            for (int i = 0, at = 0; i < placed.Count; i++)
            {
                var o = placed[i];
                var def = lib.Objects[o.Def];
                ObjectField.ContactEnds(lib, o, out float floorY, out float ceilY);
                bool onField = false;
                for (int p = 0; p < def.Parts.Count; p++) onField |= defPartTable[partBase[o.Def] + p].Ground == GroundField;
                objects[i] = new ObjectSpawn
                {
                    Id = o.Id, P = new float3(o.X, o.Y, o.Z), Q = new quaternion(o.Qx, o.Qy, o.Qz, o.Qw), Size = o.Size, Def = (ushort)o.Def, Variant = (ushort)o.Variant,
                    Contact = !float.IsNaN(floorY) && !float.IsNaN(ceilY) ? floorY - ceilY : o.Size, Ground = float.IsNaN(floorY) ? ceilY : floorY, Field = onField ? field.GroundAt(o.X, o.Z) : 0f,
                };
                long key = Key(o.Def, (int)(o.X / tile[o.Def]), (int)(o.Z / tile[o.Def]));
                for (int p = 0; p < lib.Objects[o.Def].Parts.Count; p++) order[at++] = new SortKey { Key = key, Item = (i << 8) | p };
            }
            order.Sort();
            int distinct = 0;
            for (int i = 0; i < n; i++) if (i == 0 || order[i].Key != order[i - 1].Key) distinct++;
            var keys = new NativeArray<long>(distinct, Allocator.Persistent);
            var spans = new NativeArray<int3>(distinct, Allocator.Persistent);
            for (int i = 0, d = -1; i < n; i++)
            {
                if (i == 0 || order[i].Key != order[i - 1].Key) { d++; keys[d] = order[i].Key; spans[d] = new int3(i, 0, c); }
                spans[d] += new int3(0, 1, 0);
                parts[i] = (uint)order[i].Item;
            }
            order.Dispose();
            return new ChunkParts { Chunk = c, Base = minBase, Keys = keys, Ranges = spans, Objects = objects, Parts = parts, Lights = emitters };
        }

        List<LightEmitter> LightsIn(List<ObjectInstance> placed)
        {
            List<LightEmitter> lights = null;
            var anchors = anchorScratch ??= new List<StructureLight>();
            foreach (var o in placed)
            {
                anchors.Clear();
                field.LightsOf(o, anchors);
                for (int k = 0; k < anchors.Count; k++)
                {
                    var a = anchors[k];
                    var light = a.Shape < 0 ? lib.Objects[o.Def].Light : lib.EmitterFor(a.Shape)?.Light;
                    var kind = light == null ? null : lib.LightNamed(light.Category);
                    if (kind == null) continue;
                    var e = LightEmitter.Of(kind, new float3(a.X, a.Y, a.Z), Decode(light.Color), light.RangeM, light.Intensity);
                    if (light.Importance > 0) e.Importance = light.Importance;
                    e.Flicker = light.Flicker;
                    e.FlickerHz = light.FlickerHz;
                    e.Sky = light.Daylight;
                    e.Coreless = !light.Core;
                    e.ConeDeg = light.ConeDeg;
                    e.Phase = ((o.Id + (ulong)k) * 0x9E3779B97F4A7C15UL >> 40) / 16777216f;
                    (lights ??= new List<LightEmitter>()).Add(e);
                }
            }
            return lights;
        }

        void DestroyLights(List<Entity> lights)
        {
            if (lights == null) return;
            foreach (var e in lights) if (EntityManager.Exists(e)) EntityManager.DestroyEntity(e);
        }

        struct SortKey : IComparable<SortKey>
        {
            public long Key;
            public int Item;
            public int CompareTo(SortKey o) => Key != o.Key ? Key.CompareTo(o.Key) : Item.CompareTo(o.Item);
        }

        static float RectDistance(float3 cam, float x0, float z0, float size)
        {
            float dx = math.max(0, math.max(x0 - cam.x, cam.x - (x0 + size)));
            float dz = math.max(0, math.max(z0 - cam.z, cam.z - (z0 + size)));
            return math.sqrt(dx * dx + dz * dz);
        }

        void LoadChunks(float3 cam, float maxReach)
        {
            for (int t = chunkTasks.Count - 1; t >= 0; t--)
            {
                var task = chunkTasks[t];
                if (!task.IsCompleted) continue;
                chunkTasks.RemoveAt(t);
                if (task.IsFaulted) { GameStatus.Failure = task.Exception.ToString(); continue; }
                var r = task.Result;
                collectingChunk[r.Chunk] = false;
                bool again = loaded[r.Chunk];
                if (again) recollectMaxMs = math.max(recollectMaxMs, r.Ms);
                else firstMaxMs = math.max(firstMaxMs, r.Ms);
                if (blockedSince[r.Chunk] >= 0f)
                {
                    if (blockedFrame[r.Chunk] >= UnityEngine.Time.frameCount - 1) partsWaitMax = math.max(partsWaitMax, (UnityEngine.Time.realtimeSinceStartup - blockedSince[r.Chunk]) * 1000f);
                    blockedSince[r.Chunk] = -1f;
                }
                if (again)
                {
                    DropParts(r.Chunk);
                    recollects++;
                }
                for (int i = 0; i < r.Keys.Length; i++)
                    if (!ranges.TryAdd(r.Keys[i], r.Ranges[i])) r.Keys[i] = -1;
                r.Ranges.Dispose();
                chunkParts[r.Chunk] = r.Parts;
                chunkObjects[r.Chunk] = r.Objects;
                chunkKeys[r.Chunk] = r.Keys;
                heldBase[r.Chunk] = r.Base;
                partsHeld += r.Parts.Length;
                objectsHeld += r.Objects.Length;
                peakParts = math.max(peakParts, partsHeld);
                if (again) continue;
                loaded[r.Chunk] = true;
                if (r.Lights != null)
                {
                    var made = new List<Entity>(r.Lights.Count);
                    foreach (var light in r.Lights)
                    {
                        var e = EntityManager.CreateEntity();
                        EntityManager.AddComponentData(e, light);
                        made.Add(e);
                    }
                    chunkLights[r.Chunk] = made;
                }
                chunksLoaded++;
                chunksHeld++;
                peakChunks = math.max(peakChunks, chunksHeld);
            }
            float q = math.max(reachQuality, 1e-3f), ring = maxReach + span;
            for (int c = 0; c < loaded.Length; c++)
            {
                if (!loaded[c] || collectingChunk[c]) continue;
                float dist = RectDistance(cam, (c % across) * span, (c / across) * span, span);
                if (dist <= ring * UnloadReach)
                {
                    float want = Need(dist, q);
                    if ((Grows(dist, q, heldBase[c]) || want > heldBase[c] * ShrinkAt) && SameSet(heldBase[c], want)) heldBase[c] = want;
                    continue;
                }
                DropParts(c);
                DestroyLights(chunkLights[c]);
                chunkLights[c] = null;
                loaded[c] = false;
                chunksHeld--;
                chunksUnloaded++;
            }
            bool unserved = false;
            while (true)
            {
                int best = -1;
                float bestD = ring, bestShrink = float.MaxValue;
                int shrink = -1;
                for (int c = 0; c < loaded.Length; c++)
                {
                    if (collectingChunk[c]) continue;
                    float d = RectDistance(cam, (c % across) * span, (c / across) * span, span);
                    if (loaded[c])
                    {
                        if (Grows(d, q, heldBase[c])) { if (d < bestD) { bestD = d; best = c; } }
                        else if (Need(d, q) > heldBase[c] * ShrinkAt && d < bestShrink) { bestShrink = d; shrink = c; }
                        continue;
                    }
                    if (d < bestD) { bestD = d; best = c; }
                }
                int pick = best >= 0 ? best : shrink;
                if (pick < 0) break;
                if (chunkTasks.Count >= Collectors) { unserved = best >= 0; break; }
                collectingChunk[pick] = true;
                float minBase = Need(RectDistance(cam, (pick % across) * span, (pick / across) * span, span), q);
                chunkTasks.Add(StartCollect(pick, minBase, !loaded[pick]));
            }
            why = (chunkTasks.Count > 0 ? 1 : 0) | (unserved ? 2 : 0);
            Pending = why != 0 ? 1 : 0;
        }

        const float ShrinkAt = 2f;

        float Need(float dist, float q) => math.max(0f, dist - span) / (q * UnloadReach);

        bool Grows(float dist, float q, float held) => math.max(0f, dist - span) / q < held;

        bool SameSet(float a, float b)
        {
            float lo = math.min(a, b), hi = math.max(a, b);
            foreach (float r in baseReach) if (r >= lo && r < hi) return false;
            return true;
        }

        void DropParts(int c)
        {
            foreach (long key in chunkKeys[c]) if (key >= 0) ranges.Remove(key);
            partsHeld -= chunkParts[c].Length;
            objectsHeld -= chunkObjects[c].Length;
            chunkParts[c].Dispose();
            chunkObjects[c].Dispose();
            chunkKeys[c].Dispose();
        }

        Task<ChunkParts> StartCollect(int c, float minBase, bool lights) => Task.Run(() => Collect(c, minBase, lights));

        public const float StreamMs = 2.5f;

        static float Since(long t0) => (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000f / System.Diagnostics.Stopwatch.Frequency;

        void Despawn()
        {
            var doomed = new NativeList<Entity>(Allocator.Temp);
            foreach (long key in doomedKeys)
            {
                if (!spawned.Contains(key)) continue;
                int d = (int)(key >> 40);
                if (doomed.Length >= DespawnBudget) { Pending = 1; why |= 4; break; }
                int count = doomed.Length;
                if (tileEntities.TryGetFirstValue(key, out var e, out var it))
                    do doomed.Add(e); while (tileEntities.TryGetNextValue(out e, ref it));
                count = doomed.Length - count;
                tileEntities.Remove(key);
                spawned.Remove(key);
                liveByDef[d] -= count;
            }
            if (doomed.Length > 0) EntityManager.DestroyEntity(doomed.AsArray());
            liveEntities -= doomed.Length;
            doomed.Dispose();
        }

        int Spawn(long t0)
        {
            int budget = SpawnBudget, waiting = 0, frame = UnityEngine.Time.frameCount;
            float now = UnityEngine.Time.realtimeSinceStartup;
            bool watch = settled && !GameStatus.AfterTool;
            writes = new NativeList<SpawnWrite>(256, Allocator.TempJob);
            for (int i = 0; i < wantKeys.Length; i++)
            {
                long key = wantKeys[i];
                if (spawned.Contains(key)) continue;
                int d = (int)(key >> 40), tx = (int)((key >> 20) & 0xFFFFF), tz = (int)(key & 0xFFFFF);
                float t = tile[d];
                int c = math.min(across - 1, (int)(tz * t / span)) * across + math.min(across - 1, (int)(tx * t / span));
                if (!loaded[c] || baseReach[d] < heldBase[c])
                {
                    waiting++;
                    why |= 8;
                    if (watch && blockedFrame[c] != frame)
                    {
                        if (blockedSince[c] < 0f || blockedFrame[c] < frame - 1) blockedSince[c] = now;
                        blockedFrame[c] = frame;
                    }
                    continue;
                }
                if (!ranges.TryGetValue(key, out var range)) { spawned.Add(key); continue; }
                if (budget <= 0 || Since(t0) > StreamMs) { waiting++; why |= budget <= 0 ? 16 : 32; continue; }
                pickKeys.Add(key);
                pickRanges.Add(range);
                budget -= range.y;
            }
            if (pickKeys.Count > 0) SpawnPicked();
            if (writes.Length > 0)
                Dependency = new SpawnJob
                {
                    Writes = writes.AsArray(), Defs = defLods, Ids = meshIdsNative, Bounds = boundsNative, Slots = slotsNative, DefParts = defPartsNative, ColorBase = colorNative, LeafBase = leafNative, Shadows = shadowNative,
                    Material = materialId, Foliage = foliageMaterialId,
                    Ltw = GetComponentLookup<LocalToWorld>(), Rb = GetComponentLookup<RenderBounds>(), Mmi = GetComponentLookup<MaterialMeshInfo>(),
                    Color = GetComponentLookup<ObjectColor>(), Fade = GetComponentLookup<ObjectFade>(), Leaf = GetComponentLookup<ObjectLeaf>(), Lod = GetComponentLookup<ObjectLod>(),
                }.Schedule(writes.Length, 64, Dependency);
            writes.Dispose(Dependency);
            return waiting;
        }

        void SpawnPicked()
        {
            var counts = groupCounts;
            System.Array.Clear(counts, 0, 4);
            System.Array.Clear(groupAt, 0, 4);
            for (int t = 0; t < pickKeys.Count; t++)
            {
                var range = pickRanges[t];
                var objects = chunkObjects[range.z];
                var parts = chunkParts[range.z];
                for (int k = 0; k < range.y; k++)
                {
                    uint item = parts[range.x + k];
                    var o = objects[(int)(item >> 8)];
                    if (!Hidden(o.Id)) counts[Group(meshBase[o.Def][(int)(item & 255)] + o.Variant)]++;
                }
            }
            var prefabs = groupPrefabs;
            prefabs[0] = flatPrefab; prefabs[1] = castPrefab; prefabs[2] = flatFoliagePrefab; prefabs[3] = castFoliagePrefab;
            var made = groupMade;
            for (int g = 0; g < 4; g++) made[g] = EntityManager.Instantiate(prefabs[g], counts[g], Allocator.Temp);
            var at = groupAt;
            for (int t = 0; t < pickKeys.Count; t++)
            {
                long key = pickKeys[t];
                var range = pickRanges[t];
                var objects = chunkObjects[range.z];
                var parts = chunkParts[range.z];
                int shown = 0;
                for (int k = 0; k < range.y; k++)
                {
                    uint item = parts[range.x + k];
                    var s = objects[(int)(item >> 8)];
                    if (Hidden(s.Id)) continue;
                    shown++;
                    int p = (int)(item & 255);
                    int group = Group(meshBase[s.Def][p] + s.Variant);
                    var e = made[group][at[group]++];
                    writes.Add(new SpawnWrite { E = e, S = s, Part = partBase[s.Def] + p, Foliage = group >= 2 });
                    tileEntities.Add(key, e);
                    liveByDef[s.Def]++;
                }
                spawned.Add(key);
                liveEntities += shown;
            }
            foreach (var arr in made) arr.Dispose();
            pickKeys.Clear();
            pickRanges.Clear();
        }

        JobHandle Scan(float3 cam) => new ScanJob
        {
            Cam = cam, WorldM = worldM, Despawn = DespawnReach, Tile = tileNative, Reach = reachNative, Spawned = spawned, Doomed = doomedKeys, Want = wantKeys,
        }.Schedule();

        [BurstCompile]
        struct ScanJob : IJob
        {
            public float3 Cam;
            public float WorldM, Despawn;
            [ReadOnly] public NativeArray<float> Tile, Reach;
            [ReadOnly] public NativeParallelHashSet<long> Spawned;
            public NativeList<long> Doomed, Want;

            public void Execute()
            {
                Doomed.Clear();
                Want.Clear();
                foreach (long key in Spawned)
                {
                    int d = (int)(key >> 40), tx = (int)((key >> 20) & 0xFFFFF), tz = (int)(key & 0xFFFFF);
                    if (RectDistance(Cam, tx * Tile[d], tz * Tile[d], Tile[d]) > Reach[d] * Despawn) Doomed.Add(key);
                }
                for (int d = 0; d < Tile.Length; d++)
                {
                    float t = Tile[d], r = Reach[d];
                    int tiles = (int)math.ceil(WorldM / t);
                    int x0 = math.max(0, (int)((Cam.x - r) / t)), x1 = math.min(tiles - 1, (int)((Cam.x + r) / t));
                    int z0 = math.max(0, (int)((Cam.z - r) / t)), z1 = math.min(tiles - 1, (int)((Cam.z + r) / t));
                    for (int tz = z0; tz <= z1; tz++)
                        for (int tx = x0; tx <= x1; tx++)
                        {
                            if (RectDistance(Cam, tx * t, tz * t, t) > r) continue;
                            long key = Key(d, tx, tz);
                            if (!Spawned.Contains(key)) Want.Add(key);
                        }
                }
            }
        }

        struct DefLod
        {
            public int Lods;
            public Detail.Rule Rule;
            public float Impostor;
        }

        struct SpawnWrite
        {
            public Entity E;
            public ObjectSpawn S;
            public int Part;
            public bool Foliage;
        }

        [BurstCompile]
        struct SpawnJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<SpawnWrite> Writes;
            [ReadOnly] public NativeArray<DefLod> Defs;
            [ReadOnly] public NativeArray<BatchMeshID> Ids;
            [ReadOnly] public NativeArray<AABB> Bounds;
            [ReadOnly] public NativeArray<int> Slots;
            [ReadOnly] public NativeArray<ObjectDefPart> DefParts;
            [ReadOnly] public NativeArray<float4> ColorBase, LeafBase;
            [ReadOnly] public NativeArray<bool> Shadows;
            public BatchMaterialID Material, Foliage;
            [NativeDisableParallelForRestriction] public ComponentLookup<LocalToWorld> Ltw;
            [NativeDisableParallelForRestriction] public ComponentLookup<RenderBounds> Rb;
            [NativeDisableParallelForRestriction] public ComponentLookup<MaterialMeshInfo> Mmi;
            [NativeDisableParallelForRestriction] public ComponentLookup<ObjectColor> Color;
            [NativeDisableParallelForRestriction] public ComponentLookup<ObjectFade> Fade;
            [NativeDisableParallelForRestriction] public ComponentLookup<ObjectLeaf> Leaf;
            [NativeDisableParallelForRestriction] public ComponentLookup<ObjectLod> Lod;

            public void Execute(int i)
            {
                var w = Writes[i];
                var s = w.S;
                var dp = DefParts[w.Part];
                var k = Defs[s.Def];
                int mesh = dp.Mesh + s.Variant, m = mesh * Stride;
                bool imp = w.Foliage && k.Impostor > 0 && Slots[mesh] >= 0;
                bool shadow = Shadows[mesh];
                var c = ColorBase[mesh];
                var scale = dp.Scale * s.Size;
                float ground = dp.Ground == GroundDungeon ? s.P.y + Structures.DungeonFloorY : dp.Ground == GroundField ? s.Field : s.Ground;
                Ltw[w.E] = new LocalToWorld { Value = math.mul(float4x4.TRS(s.P, s.Q, new float3(1)), float4x4.TRS(dp.Offset * s.Size, quaternion.identity, scale)) };
                Rb[w.E] = new RenderBounds { Value = Bounds[mesh] };
                Mmi[w.E] = new MaterialMeshInfo(w.Foliage ? Foliage : Material, Ids[m]);
                Color[w.E] = new ObjectColor { Value = new float4(c.xyz * (0.86f + 0.28f * math.frac(s.Variant * 0.618034f + s.Size * 3.7f + s.P.x * 0.013f)), c.w) };
                Fade[w.E] = new ObjectFade { Value = new float4(1, shadow ? 1 : 0, ground, s.Contact) };
                if (w.Foliage)
                {
                    var l = LeafBase[mesh];
                    float leafTint = 0.88f + 0.24f * math.frac(s.Variant * 0.381966f + s.P.z * 0.0171f + s.Size * 1.3f);
                    float hue = 2f * math.frac(s.Variant * 0.7548777f + s.P.x * 0.0093f + s.P.z * 0.0061f) - 1f;
                    Leaf[w.E] = new ObjectLeaf { Value = new float4(l.xyz * leafTint * (1f + hue * new float3(0.16f, 0.06f, -0.13f)), l.w) };
                }
                var rule = k.Rule;
                if (!shadow) rule.ShadowCascadePixels = rule.ShadowPixels = Detail.Never;
                Lod[w.E] = new ObjectLod
                {
                    L0 = Ids[m], L1 = Ids[m + math.min(1, k.Lods - 1)], L2 = Ids[m + math.min(2, k.Lods - 1)], Rule = rule, Radius = 0.5f * math.cmax(scale),
                    Imp = imp ? Ids[m + Lods] : default, Blend = imp ? Ids[m + Lods + 1] : default, Impostor = imp ? k.Impostor : 0,
                };
            }
        }

        int Group(int mesh) => (shadowMesh[mesh] ? 1 : 0) + (foliageMaterial != null && foliageMesh[mesh] ? 2 : 0);

        bool Warm()
        {
            if (material == null)
            {
                material = new Material(Shader.Find("Stabby/Object")) { name = "Stabby objects", enableInstancing = true };
                return false;
            }
            if (materialId == BatchMaterialID.Null)
            {
                materialId = World.GetExistingSystemManaged<EntitiesGraphicsSystem>().RegisterMaterial(material);
                return false;
            }
            return true;
        }

        protected override void OnUpdate()
        {
            if (!JobPoll.Done(scan)) { PendingWhy |= 64; return; }
            DespawnHidden();
            if (!Warm()) { PendingWhy = 128; return; }
            if (field == null && !Prepare()) { PendingWhy = 128; return; }
            using (RegisterMarker.Auto()) if (!Register()) return;
            var cam = Camera.main;
            if (cam == null) return;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var view = DetailView.Refresh(cam);
            float3 pos = view.Camera;
            if (view.Quality != reachQuality)
            {
                reachQuality = view.Quality;
                for (int d = 0; d < reach.Length; d++) reachNative[d] = reach[d] = baseReach[d] * reachQuality;
                scanned = false;
            }
            float maxReach = 0;
            foreach (float r in reach) maxReach = math.max(maxReach, r);
            if (!scanned) { Pending = 1; PendingWhy = 256; scanAt = pos; scan = Scan(pos); scanned = true; return; }
            if (math.distance(pos, lastPos) > span) settled = false;
            lastPos = pos;
            using (LoadMarker.Auto()) LoadChunks(pos, maxReach);
            using (DespawnMarker.Auto()) Despawn();
            int waiting;
            using (SpawnMarker.Auto()) waiting = Spawn(t0);
            if (waiting > 0) Pending = 1;
            if (waiting == 0 && GameStatus.TerrainBuilt) settled = true;
            if (Pending == 0) { readyAt = scanAt; readyFrame = UnityEngine.Time.frameCount; }
            PendingWhy = why;
            scanAt = pos;
            scan = Scan(pos);
            scanned = true;
            LiveEntities = liveEntities;
            PartsHeld = partsHeld;
            ObjectsHeld = objectsHeld;
            Recollects = recollects;
            FirstCollectMaxMs = firstMaxMs;
            RecollectMaxMs = recollectMaxMs;
            PartsWaitMaxMs = partsWaitMax;
            ChunksHeld = chunksHeld;
            ChunksUnloaded = chunksUnloaded;
            liveByDefShared = liveByDef;
            libShared = lib;
            LastMs = Since(t0);
            if (GameStatus.TerrainBuilt && !GameStatus.AfterTool) worstUpdate = math.max(worstUpdate, LastMs);
            if (!GameStatus.Live.ContainsKey("~objects view")) GameStatus.Live["~objects view"] = Status;
        }

        string Status() => $"objects: {liveEntities} entities in {spawned.Count()} tiles, {meshes.Length} meshes; chunks {chunksHeld} held (peak {peakChunks}), {chunksLoaded} loaded, {chunksUnloaded} unloaded; parts {partsHeld} held (peak {peakParts}) in {objectsHeld} objects, {PartBytes / (1024 * 1024)} MB, {recollects} re-collects, slowest first collect {firstMaxMs:F0} ms, slowest re-collect {recollectMaxMs:F0} ms, worst wait for parts in view {partsWaitMax:F0} ms; worst view update after terrain built {worstUpdate:F1} ms";
    }

    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(ObjectViewSystem))]
    public partial struct ObjectLodSystem : ISystem
    {
        ComponentLookup<MaterialMeshInfo> mmi;
        ComponentLookup<ObjectFade> fade;
        ComponentLookup<ObjectLeaf> leaf;
        ComponentLookup<ObjectImp> imp;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ObjectLod>();
            mmi = state.GetComponentLookup<MaterialMeshInfo>();
            fade = state.GetComponentLookup<ObjectFade>();
            leaf = state.GetComponentLookup<ObjectLeaf>();
            imp = state.GetComponentLookup<ObjectImp>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            mmi.Update(ref state);
            var view = DetailView.Current;
            if (view.PixelsPerMetre <= 0) return;
            fade.Update(ref state);
            leaf.Update(ref state);
            imp.Update(ref state);
            state.Dependency = new LodJob { View = view, Mmi = mmi, Fade = fade, Leaf = leaf, Imp = imp }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithOptions(EntityQueryOptions.IncludePrefab)]
        [WithNone(typeof(Prefab))]
        partial struct LodJob : IJobEntity
        {
            public DetailView View;
            [NativeDisableParallelForRestriction] public ComponentLookup<MaterialMeshInfo> Mmi;
            [NativeDisableParallelForRestriction] public ComponentLookup<ObjectFade> Fade;
            [NativeDisableParallelForRestriction] public ComponentLookup<ObjectLeaf> Leaf;
            [NativeDisableParallelForRestriction] public ComponentLookup<ObjectImp> Imp;

            void Execute(Entity e, in LocalToWorld ltw, ref ObjectLod lod)
            {
                float3 at = ltw.Value.c3.xyz;
                float px = View.ScreenSize(2f * lod.Radius, at);
                var d = View.Decide(lod.Rule, 2f * lod.Radius, at, lod.Decision);
                lod.Decision = d;
                var t = lod.Rule.Geometry.Scaled(View.Quality);
                bool on = px >= t.Far;
                if (Mmi.IsComponentEnabled(e) != on) Mmi.SetComponentEnabled(e, on);
                if (!on) return;
                var m = Mmi[e];
                int tier = math.min(d.Geometry, 2);
                var id = tier == 0 ? lod.L0 : tier == 1 ? lod.L1 : lod.L2;
                if (lod.Impostor > 0)
                {
                    float ip = lod.Impostor * View.Impostor / math.max(View.Quality, 0.1f);
                    float b = tier == 2 ? math.round((1f - Detail.Crossfade(px, ip, 1.5f * ip)) * 16f) / 16f : 0;
                    if (b >= 1) id = lod.Imp;
                    else if (b > 0) id = lod.Blend;
                    if (Imp[e].Value != b) Imp[e] = new ObjectImp { Value = b };
                }
                if (m.MeshID.value != id.value) { m.MeshID = id; Mmi[e] = m; }
                var old = Fade[e].Value;
                float shadow = d.Shadow == Detail.ShadowCascade ? 1f : d.Shadow == Detail.ShadowLow ? 0.75f : 0f;
                var f = new float4(math.round(Detail.Crossfade(px, t.Far, 1.5f * t.Far) * 16f) / 16f, shadow, old.z, old.w);
                if (math.any(old != f)) Fade[e] = new ObjectFade { Value = f };
                if (!Leaf.HasComponent(e)) return;
                var a = lod.Rule.Animation.Scaled(View.Quality);
                float sway = d.Animation <= Detail.AnimSway ? Detail.Crossfade(px, a.Far, 1.5f * a.Far) : 0;
                float flutter = d.Animation == Detail.AnimFull ? Detail.Crossfade(px, a.Near, 1.5f * a.Near) : 0;
                float w = math.round((sway + flutter) * 16f) / 16f;
                var lv = Leaf[e].Value;
                if (lv.w != w) { lv.w = w; Leaf[e] = new ObjectLeaf { Value = lv }; }
            }
        }
    }
}
