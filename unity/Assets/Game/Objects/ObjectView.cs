using System;
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
        public float4x4 M;
        public float4 Color, Leaf;
        public int Mesh, Def;
        public float Radius, Ground, Size;
        public bool Shadow;
        public ulong Id;
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class ObjectViewSystem : SystemBase
    {
        public const int Lods = 3, Stride = Lods + 2, SpawnBudget = 8000, DespawnBudget = 8000, Frames = 8, Cell = 64;
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

            public void Dispose()
            {
                if (Pack != null)
                {
                    Pack.Dispose();
                    return;
                }
                if (Vertices.IsCreated) Vertices.Dispose();
                if (Narrow.IsCreated) Narrow.Dispose();
                if (Wide.IsCreated) Wide.Dispose();
            }
        }

        struct ChunkParts
        {
            public int Chunk;
            public NativeArray<long> Keys;
            public NativeArray<int3> Ranges;
            public NativeArray<ObjectSpawn> Parts;
            public List<LightEmitter> Lights;

            public void Dispose()
            {
                Keys.Dispose();
                Ranges.Dispose();
                Parts.Dispose();
            }
        }

        ObjectField field;
        ObjectLibrary lib;
        float cm, worldM, span;
        int across;
        int[][] meshBase;
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
        bool[] foliageMesh;
        Task<LeafAtlas.Pixels> atlasTask;
        Texture2D atlas;
        RenderTexture impA, impB;
        readonly List<Task<ChunkParts>> chunkTasks = new List<Task<ChunkParts>>();
        bool[] collectingChunk;
        static readonly int Collectors = math.max(1, Environment.ProcessorCount / 4);
        NativeArray<DefLod> defLods;
        NativeArray<BatchMeshID> meshIdsNative;
        NativeArray<AABB> boundsNative;
        NativeArray<int> slotsNative;
        NativeList<SpawnWrite> writes;
        readonly int[] groupCounts = new int[4], groupAt = new int[4];
        readonly NativeArray<Entity>[] groupMade = new NativeArray<Entity>[4];
        readonly Entity[] groupPrefabs = new Entity[4];
        NativeArray<bool> loaded;
        NativeArray<ObjectSpawn>[] chunkParts;
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
        int liveEntities, chunksLoaded, chunksHeld, chunksUnloaded, partsHeld, peakParts, peakChunks;
        int[] liveByDef;
        ObjectKind[] kinds;
        Detail.Rule[] rules;
        float worstUpdate;
        static readonly Unity.Profiling.ProfilerMarker LoadMarker = new Unity.Profiling.ProfilerMarker("ObjectView.LoadChunks"), DespawnMarker = new Unity.Profiling.ProfilerMarker("ObjectView.Despawn"), SpawnMarker = new Unity.Profiling.ProfilerMarker("ObjectView.Spawn"), RegisterMarker = new Unity.Profiling.ProfilerMarker("ObjectView.Register");

        public static int Pending { get; private set; } = 1;
        static float3 readyAt;
        static int readyFrame = -1;
        public static bool ReadyAround(float3 p, float within) => readyFrame >= UnityEngine.Time.frameCount - 2 && math.distance(readyAt, p) <= within;
        public static int LiveEntities { get; private set; }
        public static int PartsHeld { get; private set; }
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
            if (chunkKeys != null) foreach (var a in chunkKeys) if (a.IsCreated) a.Dispose();
            if (chunkLights != null) foreach (var a in chunkLights) DestroyLights(a);
            foreach (var t in chunkTasks) Owned.Release(t, r => r.Dispose());
            Owned.Release(meshTask, r => r.Dispose());
            meshBatch?.Dispose();
            meshSet?.Dispose();
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

        public const float GlowBase = 0.05f, GlowSpan = 0.2f, GlowPatternStep = 0.22f, IceBase = 0.48f, IceSpan = 0.015f;

        static float4 Linear(uint rgb, float tint, int shape, float glow, int pattern, float translucency)
        {
            if (translucency > 0f) return new float4(Decode(rgb) * tint, IceBase + IceSpan * translucency);
            if (glow > 0f) return new float4(Decode(rgb) * tint, GlowBase + GlowSpan * glow + GlowPatternStep * pattern);
            if (ObjectPart.IsStone(shape)) return new float4(tint, tint, tint, 0);
            return new float4(Decode(rgb) * tint, shape);
        }

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
            chunkParts = new NativeArray<ObjectSpawn>[across * across];
            chunkKeys = new NativeArray<long>[across * across];
            chunkLights = new List<Entity>[across * across];
            collectingChunk = new bool[across * across];
            float minTile = cm * 0.5f;
            int defs = lib.Objects.Count, count = 0;
            meshBase = new int[defs][];
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
            for (int d = 0; d < defs; d++)
            {
                var def = lib.Objects[d];
                var kind = lib.KindOf(def);
                meshBase[d] = new int[def.Parts.Count];
                for (int p = 0; p < def.Parts.Count; p++)
                {
                    meshBase[d][p] = count;
                    for (int v = 0; v < def.Variants; v++)
                    {
                        shapes.Add(def.Parts[p].Shape); parts.Add(def.Parts[p]); keys.Add(ObjectMeshes.Key(def.Id, p, v));
                        impostors.Add(kind.ImpostorPixels > 0 && ObjectPart.IsFoliage(def.Parts[p].Shape));
                        count++;
                    }
                }
                kinds[d] = kind;
                rules[d] = lib.RuleOf(kind);
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
            bool anyFoliage = false;
            for (int k = 0; k < shapes.Count; k++) anyFoliage |= foliageMesh[k] = ObjectPart.IsFoliage(shapes[k]);
            meshTask = Task.Run(() => BuildMeshes(shapes, parts, keys, impostors));
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

        static MeshSet BuildMeshes(List<int> shapes, List<ObjectPart> parts, List<ulong> keys, List<bool> impostors)
        {
            int n = shapes.Count * Stride;
            var set = new MeshSet
            {
                Vertices = new NativeList<float>(1 << 20, Allocator.Persistent), Narrow = new NativeList<ushort>(1 << 18, Allocator.Persistent), Wide = new NativeList<int>(0, Allocator.Persistent),
                Spans = new MeshSpan[n], Bounds = new AABB[shapes.Count], Centers = new Vector4[shapes.Count], Slots = new int[shapes.Count],
            };
            try
            {
                for (int i = 0; i < n; i++) set.Spans[i].Uvs = -1;
                for (int k = 0; k < shapes.Count; k++)
                {
                    for (int l = 0; l < Lods; l++)
                    {
                        var m = ObjectMeshes.Build(parts[k], keys[k], l);
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
                Pack(set);
            }
            catch
            {
                set.Dispose();
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

        static void Pack(MeshSet set)
        {
            var pack = new MeshPack(set.Vertices, set.Narrow, set.Wide);
            set.Pack = pack;
            var attr = new int[Layouts.Length];
            for (int u = 0; u < Layouts.Length; u++) attr[u] = pack.Layout(Layouts[u]);
            for (int i = 0; i < set.Spans.Length; i++)
            {
                var s = set.Spans[i];
                if (s.Uvs < 0) continue;
                pack.Add("Object " + i, new MeshPiece
                {
                    Id = i, Vertex = s.Vertex, VertexCount = s.VertexCount, Floats = Floats(s.Uvs), Index = s.Index, IndexCount = s.IndexCount, Attr = attr[s.Uvs], AttrCount = Layouts[s.Uvs].Length, Wide = s.Wide,
                });
            }
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
            if (meshSet == null)
            {
                if (meshTask == null || !meshTask.IsCompleted) return false;
                if (meshTask.IsFaulted) { GameStatus.Failure = meshTask.Exception.ToString(); meshTask = null; return false; }
                meshSet = meshTask.Result;
                meshTask = null;
                meshes = new Mesh[meshSet.Spans.Length];
                meshIds = new BatchMeshID[meshes.Length];
            }
            if (atlasTask != null)
            {
                if (!atlasTask.IsCompleted) return false;
                if (atlasTask.IsFaulted) { GameStatus.Failure = atlasTask.Exception.ToString(); atlasTask = null; return false; }
                atlas = LeafAtlas.Create(atlasTask.Result);
                atlasTask = null;
            }
            if (meshesDone) return true;
            var graphics = World.GetExistingSystemManaged<EntitiesGraphicsSystem>();
            meshBatch ??= new MeshBatch(meshSet.Pack);
            if (meshBatch.Pump(pumped, pumpedIds))
                for (int i = 0; i < pumped.Count; i++)
                {
                    meshes[pumpedIds[i]] = pumped[i];
                    meshIds[pumpedIds[i]] = graphics.RegisterMesh(pumped[i]);
                }
            if (!meshBatch.Done) return false;
            meshBatch.Dispose();
            meshBatch = null;
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

        ChunkParts Collect(int c)
        {
            var placed = ListPool.Rent<ObjectInstance>();
            try { return Collect(c, placed); }
            finally { ListPool.Return(placed); }
        }

        ChunkParts Collect(int c, List<ObjectInstance> placed)
        {
            int cx = c % across, cz = c / across;
            field.PlaceInto(cx, cz, placed);
            int n = 0;
            for (int i = 0; i < placed.Count; i++) n += lib.Objects[placed[i].Def].Parts.Count;
            var order = new NativeArray<SortKey>(n, Allocator.Persistent);
            var parts = new NativeArray<ObjectSpawn>(n, Allocator.Persistent);
            for (int i = 0, at = 0; i < placed.Count; i++)
            {
                var o = placed[i];
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
                var o = placed[order[i].Item >> 8];
                int p = order[i].Item & 255;
                var def = lib.Objects[o.Def];
                var part = def.Parts[p];
                var root = float4x4.TRS(new float3(o.X, o.Y, o.Z), new quaternion(o.Qx, o.Qy, o.Qz, o.Qw), new float3(1));
                float tint = 0.86f + 0.28f * math.frac(o.Variant * 0.618034f + o.Size * 3.7f + o.X * 0.013f);
                var scale = new float3(part.ScaleX, part.ScaleY, part.ScaleZ) * o.Size;
                var local = float4x4.TRS(new float3(part.OffsetX, part.OffsetY, part.OffsetZ) * o.Size, quaternion.identity, scale);
                float leafTint = 0.88f + 0.24f * math.frac(o.Variant * 0.381966f + o.Z * 0.0171f + o.Size * 1.3f);
                float hue = 2f * math.frac(o.Variant * 0.7548777f + o.X * 0.0093f + o.Z * 0.0061f) - 1f;
                var leaf = ObjectPart.IsFoliage(part.Shape) ? new float4(Decode(part.LeafColor) * leafTint * (1f + hue * new float3(0.16f, 0.06f, -0.13f)), 2) : float4.zero;
                ObjectField.ContactEnds(lib, o, out float floorY, out float ceilY);
                bool both = !float.IsNaN(floorY) && !float.IsNaN(ceilY);
                parts[i] = new ObjectSpawn
                {
                    M = math.mul(root, local), Color = Linear(part.Color, tint, part.Shape, part.Glow, part.GlowPattern, part.Translucency), Leaf = leaf, Mesh = meshBase[o.Def][p] + o.Variant, Def = o.Def,
                    Radius = 0.5f * math.cmax(scale), Shadow = lib.PartShadows(def, part), Ground = part.Shape == ObjectPart.Dungeon ? o.Y + Structures.DungeonFloorY : ObjectPart.IsStructure(part.Shape) && !ObjectField.IsCave(def) ? field.GroundAt(o.X, o.Z) : float.IsNaN(floorY) ? ceilY : floorY, Size = both ? floorY - ceilY : o.Size,
                    Id = o.Id,
                };
            }
            order.Dispose();
            return new ChunkParts { Chunk = c, Keys = keys, Ranges = spans, Parts = parts, Lights = LightsIn(placed) };
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
                for (int i = 0; i < r.Keys.Length; i++)
                    if (!ranges.TryAdd(r.Keys[i], r.Ranges[i])) r.Keys[i] = -1;
                r.Ranges.Dispose();
                chunkParts[r.Chunk] = r.Parts;
                chunkKeys[r.Chunk] = r.Keys;
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
                partsHeld += r.Parts.Length;
                peakParts = math.max(peakParts, partsHeld);
                peakChunks = math.max(peakChunks, chunksHeld);
            }
            for (int c = 0; c < loaded.Length; c++)
            {
                if (!loaded[c] || RectDistance(cam, (c % across) * span, (c / across) * span, span) <= maxReach * UnloadReach) continue;
                foreach (long key in chunkKeys[c]) if (key >= 0) ranges.Remove(key);
                partsHeld -= chunkParts[c].Length;
                chunkParts[c].Dispose();
                chunkKeys[c].Dispose();
                DestroyLights(chunkLights[c]);
                chunkLights[c] = null;
                loaded[c] = false;
                chunksHeld--;
                chunksUnloaded++;
            }
            int best = 0;
            while (best >= 0 && chunkTasks.Count < Collectors)
            {
                best = -1;
                float bestD = maxReach;
                for (int c = 0; c < loaded.Length; c++)
                {
                    if (loaded[c] || collectingChunk[c]) continue;
                    float d = RectDistance(cam, (c % across) * span, (c / across) * span, span);
                    if (d < bestD) { bestD = d; best = c; }
                }
                if (best < 0) break;
                collectingChunk[best] = true;
                chunkTasks.Add(StartCollect(best));
            }
            Pending = best >= 0 || chunkTasks.Count > 0 ? 1 : 0;
        }

        Task<ChunkParts> StartCollect(int c) => Task.Run(() => Collect(c));

        public const float StreamMs = 2.5f;

        static float Since(long t0) => (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000f / System.Diagnostics.Stopwatch.Frequency;

        void Despawn()
        {
            var doomed = new NativeList<Entity>(Allocator.Temp);
            foreach (long key in doomedKeys)
            {
                if (!spawned.Contains(key)) continue;
                int d = (int)(key >> 40);
                if (doomed.Length >= DespawnBudget) { Pending = 1; break; }
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
            int budget = SpawnBudget, waiting = 0;
            writes = new NativeList<SpawnWrite>(256, Allocator.TempJob);
            for (int i = 0; i < wantKeys.Length; i++)
            {
                long key = wantKeys[i];
                if (spawned.Contains(key)) continue;
                int d = (int)(key >> 40), tx = (int)((key >> 20) & 0xFFFFF), tz = (int)(key & 0xFFFFF);
                float t = tile[d];
                int c = math.min(across - 1, (int)(tz * t / span)) * across + math.min(across - 1, (int)(tx * t / span));
                if (!loaded[c]) { waiting++; continue; }
                if (!ranges.TryGetValue(key, out var range)) { spawned.Add(key); continue; }
                if (budget <= 0 || Since(t0) > StreamMs) { waiting++; continue; }
                SpawnTile(key, chunkParts[range.z], range);
                budget -= range.y;
            }
            if (writes.Length > 0)
                Dependency = new SpawnJob
                {
                    Writes = writes.AsArray(), Defs = defLods, Ids = meshIdsNative, Bounds = boundsNative, Slots = slotsNative,
                    Material = materialId, Foliage = foliageMaterialId,
                    Ltw = GetComponentLookup<LocalToWorld>(), Rb = GetComponentLookup<RenderBounds>(), Mmi = GetComponentLookup<MaterialMeshInfo>(),
                    Color = GetComponentLookup<ObjectColor>(), Fade = GetComponentLookup<ObjectFade>(), Leaf = GetComponentLookup<ObjectLeaf>(), Lod = GetComponentLookup<ObjectLod>(),
                }.Schedule(writes.Length, 64, Dependency);
            writes.Dispose(Dependency);
            return waiting;
        }

        void SpawnTile(long key, NativeArray<ObjectSpawn> parts, int3 range)
        {
            var counts = groupCounts;
            System.Array.Clear(counts, 0, 4);
            System.Array.Clear(groupAt, 0, 4);
            int shown = 0;
            for (int k = 0; k < range.y; k++) if (!Hidden(parts[range.x + k].Id)) { shown++; counts[Group(parts[range.x + k])]++; }
            var prefabs = groupPrefabs;
            prefabs[0] = flatPrefab; prefabs[1] = castPrefab; prefabs[2] = flatFoliagePrefab; prefabs[3] = castFoliagePrefab;
            var made = groupMade;
            for (int g = 0; g < 4; g++) made[g] = EntityManager.Instantiate(prefabs[g], counts[g], Allocator.Temp);
            var at = groupAt;
            for (int k = 0; k < range.y; k++)
            {
                var s = parts[range.x + k];
                if (Hidden(s.Id)) continue;
                int group = Group(s);
                var e = made[group][at[group]++];
                writes.Add(new SpawnWrite { E = e, S = s, Foliage = group >= 2 });
                tileEntities.Add(key, e);
                liveByDef[s.Def]++;
            }
            spawned.Add(key);
            liveEntities += shown;
            foreach (var arr in made) arr.Dispose();
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
                var k = Defs[s.Def];
                int m = s.Mesh * Stride;
                bool imp = w.Foliage && k.Impostor > 0 && Slots[s.Mesh] >= 0;
                Ltw[w.E] = new LocalToWorld { Value = s.M };
                Rb[w.E] = new RenderBounds { Value = Bounds[s.Mesh] };
                Mmi[w.E] = new MaterialMeshInfo(w.Foliage ? Foliage : Material, Ids[m]);
                Color[w.E] = new ObjectColor { Value = s.Color };
                Fade[w.E] = new ObjectFade { Value = new float4(1, s.Shadow ? 1 : 0, s.Ground, s.Size) };
                if (w.Foliage) Leaf[w.E] = new ObjectLeaf { Value = s.Leaf };
                var rule = k.Rule;
                if (!s.Shadow) rule.ShadowCascadePixels = rule.ShadowPixels = Detail.Never;
                Lod[w.E] = new ObjectLod
                {
                    L0 = Ids[m], L1 = Ids[m + math.min(1, k.Lods - 1)], L2 = Ids[m + math.min(2, k.Lods - 1)], Rule = rule, Radius = s.Radius,
                    Imp = imp ? Ids[m + Lods] : default, Blend = imp ? Ids[m + Lods + 1] : default, Impostor = imp ? k.Impostor : 0,
                };
            }
        }

        int Group(in ObjectSpawn s) => (s.Shadow ? 1 : 0) + (foliageMaterial != null && foliageMesh[s.Mesh] ? 2 : 0);

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
            if (!JobPoll.Done(scan)) return;
            DespawnHidden();
            if (!Warm()) return;
            if (field == null && !Prepare()) return;
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
            if (!scanned) { Pending = 1; scanAt = pos; scan = Scan(pos); scanned = true; return; }
            using (LoadMarker.Auto()) LoadChunks(pos, maxReach);
            using (DespawnMarker.Auto()) Despawn();
            int waiting;
            using (SpawnMarker.Auto()) waiting = Spawn(t0);
            if (waiting > 0) Pending = 1;
            if (Pending == 0) { readyAt = scanAt; readyFrame = UnityEngine.Time.frameCount; }
            scanAt = pos;
            scan = Scan(pos);
            scanned = true;
            LiveEntities = liveEntities;
            PartsHeld = partsHeld;
            ChunksHeld = chunksHeld;
            ChunksUnloaded = chunksUnloaded;
            liveByDefShared = liveByDef;
            libShared = lib;
            LastMs = Since(t0);
            if (GameStatus.TerrainBuilt && !GameStatus.AfterTool) worstUpdate = math.max(worstUpdate, LastMs);
            if (!GameStatus.Live.ContainsKey("~objects view")) GameStatus.Live["~objects view"] = Status;
        }

        string Status() => $"objects: {liveEntities} entities in {spawned.Count()} tiles, {meshes.Length} meshes; chunks {chunksHeld} held (peak {peakChunks}), {chunksLoaded} loaded, {chunksUnloaded} unloaded; parts {partsHeld} held (peak {peakParts}); worst view update after terrain built {worstUpdate:F1} ms";
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
