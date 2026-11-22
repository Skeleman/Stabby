using System.Collections.Generic;
using System.Threading.Tasks;
using Stabby.Viewer;
using Stabby.World;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace Stabby.Game
{
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateBefore(typeof(CreatureAnimSystem))]
    public partial class CreatureSpawnSystem : SystemBase
    {
        sealed class Plan
        {
            public List<CreatureShape> Shapes = new List<CreatureShape>();
            public List<float[]> Verts = new List<float[]>();
            public List<ushort[]> Index = new List<ushort[]>();
            public List<float[]> LowVerts = new List<float[]>();
            public List<ushort[]> LowIndex = new List<ushort[]>();
            public List<(CreatureDef Def, CreatureBody Body, int First, float2 At, float Heading, uint Seed)> Herd = new List<(CreatureDef, CreatureBody, int, float2, float, uint)>();
        }

        static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
        };

        public static int Count { get; private set; }

        Task<Plan> plan;
        Material material;
        BatchMaterialID materialId;
        readonly List<Mesh> meshes = new List<Mesh>();
        readonly List<BlobAssetReference<CreatureRig>> rigs = new List<BlobAssetReference<CreatureRig>>();
        bool done;

        protected override void OnCreate()
        {
            if (World.Name != GameSettings.ViewClient) { Enabled = false; return; }
            Count = 0;
        }

        protected override void OnDestroy()
        {
            Owned.Release(plan, _ => { });
            foreach (var r in rigs) if (r.IsCreated) r.Dispose();
            rigs.Clear();
            foreach (var m in meshes) if (m != null) Object.Destroy(m);
            meshes.Clear();
            if (material != null) Object.Destroy(material);
            Count = 0;
        }

        bool Warm()
        {
            if (material == null)
            {
                material = new Material(Shader.Find("Stabby/Creature")) { name = "Stabby creatures", enableInstancing = true };
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
            if (done) return;
            if (plan == null)
            {
                var maps = World.GetExistingSystemManaged<ClientMapSystem>();
                var map = maps?.Map;
                if (map == null || !SystemAPI.HasSingleton<Ground>() || !SystemAPI.TryGetSingleton<NetworkId>(out var me)) return;
                float3 at = default;
                bool found = false;
                foreach (var (t, owner) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<GhostOwner>>().WithAll<Player>())
                    if (owner.ValueRO.NetworkId == me.Value) { at = t.ValueRO.Position; found = true; break; }
                if (!found) return;
                var defs = maps.Defs;
                plan = Task.Run(() => Make(map, defs, at.xz));
                return;
            }
            if (!plan.IsCompleted) return;
            if (!Warm()) return;
            if (plan.IsFaulted) { Debug.LogException(plan.Exception); done = true; return; }
            Apply(plan.Result);
            plan = null;
            done = true;
        }

        static Plan Make(GeneratedWorld map, List<BiomeDef> defs, float2 player)
        {
            var lib = ContentFiles.Creatures(defs);
            var result = new Plan();
            var shapeStart = new Dictionary<string, int>();
            foreach (var def in lib.Bodies)
            {
                int cell = Nearest(map, def, player);
                if (cell < 0) continue;
                int n = map.Config.Size;
                float cm = map.Config.CellMeters;
                var rng = Rng.Stream(map.Config.Seed, "creature-herd-" + def.Id);
                float2 centre = new float2(cell % n + 0.5f, cell / n + 0.5f) * cm;
                int playerCell = (int)(player.y / cm) * n + (int)(player.x / cm);
                if (cell == playerCell)
                {
                    float a = rng.Range(0f, 6.2831853f);
                    centre = player + new float2(math.sin(a), math.cos(a)) * 18;
                }
                if (!shapeStart.ContainsKey(def.Id))
                {
                    shapeStart[def.Id] = result.Shapes.Count;
                    var shapes = CreatureBody.Shapes(def, lib);
                    var verts = new List<float>();
                    var index = new List<ushort>();
                    for (int s = 0; s < shapes.Count; s++)
                    {
                        CreatureMesh.Build(shapes[s], s == 0, false, verts, index);
                        result.Shapes.Add(shapes[s]);
                        result.Verts.Add(verts.ToArray());
                        result.Index.Add(index.ToArray());
                        CreatureMesh.Build(shapes[s], s == 0, true, verts, index);
                        result.LowVerts.Add(verts.ToArray());
                        result.LowIndex.Add(index.ToArray());
                    }
                }
                int count = rng.Range(def.HerdMin, def.HerdMax + 1);
                float heading = rng.Range(0f, 6.2831853f);
                for (int k = 0; k < count; k++)
                {
                    ulong seed = rng.NextU64();
                    float2 at = centre + new float2(rng.Range(-4f, 4f), rng.Range(-4f, 4f));
                    var body = CreatureBody.Assemble(def, lib, seed);
                    result.Herd.Add((def, body, shapeStart[def.Id], at, heading + rng.Range(-0.7f, 0.7f), rng.NextU32() | 1u));
                }
            }
            return result;
        }

        static int Nearest(GeneratedWorld map, CreatureDef def, float2 player)
        {
            int n = map.Config.Size;
            float cm = map.Config.CellMeters;
            int px = math.clamp((int)(player.x / cm), 0, n - 1), pz = math.clamp((int)(player.y / cm), 0, n - 1);
            int best = -1, bestScore = int.MaxValue;
            const int Window = 48;
            for (int z = math.max(0, pz - Window); z <= math.min(n - 1, pz + Window); z++)
                for (int x = math.max(0, px - Window); x <= math.min(n - 1, px + Window); x++)
                {
                    int i = z * n + x;
                    if (map.Sea[i] || map.Water.LakeId[i] >= 0 || map.Water.IsRiver[i]) continue;
                    if (!def.Biomes.Contains(map.BiomeIds[map.Biome[i]])) continue;
                    if (x == 0 || z == 0 || x == n - 1 || z == n - 1) continue;
                    float gx = map.Height[i + 1] - map.Height[i - 1], gz = map.Height[i + n] - map.Height[i - n];
                    if (gx * gx + gz * gz > 0.16f * 4 * cm * cm) continue;
                    int score = (x - px) * (x - px) + (z - pz) * (z - pz);
                    if (score < bestScore) { bestScore = score; best = i; }
                }
            return best;
        }

        static float Linear(float c) => c <= 0.04045f ? c / 12.92f : math.pow((c + 0.055f) / 1.055f, 2.4f);

        static float4 Linear(float[] c) => new float4(Linear(c[0]), Linear(c[1]), Linear(c[2]), 1);

        static float3 V(float[] a) => new float3(a[0], a[1], a[2]);

        BlobAssetReference<CreatureRig> Rig(CreatureDef def, CreatureBody body)
        {
            var b = new BlobBuilder(Allocator.Temp);
            ref var rig = ref b.ConstructRoot<CreatureRig>();
            rig.Length = body.Length;
            rig.Rear = body.RearRadius;
            rig.Stand = 0;
            rig.Stride = 1;
            rig.FadePx = def.FadePixels;
            rig.ShadowPx = def.ShadowPixels;
            rig.AnimPx = def.AnimPixels;
            rig.IkPx = def.IkPixels;
            rig.LowPx = def.LowPixels;
            var parts = b.Allocate(ref rig.Parts, body.Parts.Count);
            for (int i = 0; i < body.Parts.Count; i++)
            {
                var p = body.Parts[i];
                float reach = 0;
                for (int j = 0; j < p.SegmentCount; j++) reach += body.Segments[p.FirstSegment + j].Length;
                parts[i] = new CreaturePartRig
                {
                    Parent = p.Parent, Role = p.Role, First = p.FirstSegment, Count = p.SegmentCount,
                    Socket = new float3(p.SocketX, p.SocketY, p.SocketZ), Rest = quaternion.EulerZXY(p.RestX, p.RestY, p.RestZ),
                    Phase = p.Phase, Side = p.Side, Reach = reach,
                };
            }
            var segs = b.Allocate(ref rig.Segments, body.Segments.Count);
            for (int i = 0; i < body.Segments.Count; i++)
            {
                var s = body.Segments[i];
                segs[i] = new CreatureSegmentRig { Length = s.Length, Idle = V(s.Idle), Forward = V(s.Reach), Back = V(s.Push), Lift = V(s.Lift) };
            }
            var blob = b.CreateBlobAssetReference<CreatureRig>(Allocator.Persistent);
            b.Dispose();
            Measure(ref blob.Value, body.Size);
            return blob;
        }

        static void Measure(ref CreatureRig rig, float size)
        {
            int n = rig.Segments.Length, parts = rig.Parts.Length;
            var pos = new NativeArray<float3>(n, Allocator.Temp);
            var rot = new NativeArray<quaternion>(n, Allocator.Temp);
            var end = new NativeArray<float3>(parts, Allocator.Temp);
            var endRot = new NativeArray<quaternion>(parts, Allocator.Temp);
            CreaturePose.Solve(ref rig, 0, quaternion.identity, 0, 0, 0, pos, rot, end, endRot);
            float footY = 0, travel = 0;
            int legs = 0;
            for (int i = 0; i < parts; i++)
            {
                if (rig.Parts[i].Role != CreaturePartDef.Leg) continue;
                rig.Parts[i].Foot = end[i];
                footY += end[i].y;
                legs++;
            }
            for (int i = 0; i < parts; i++)
            {
                ref var part = ref rig.Parts[i];
                if (part.Role != CreaturePartDef.Leg) continue;
                CreaturePose.Solve(ref rig, 0, quaternion.identity, math.frac(-part.Phase), 1, 0, pos, rot, end, endRot);
                float front = end[i].z;
                CreaturePose.Solve(ref rig, 0, quaternion.identity, math.frac(0.5f - part.Phase), 1, 0, pos, rot, end, endRot);
                travel += front - end[i].z;
            }
            if (legs > 0)
            {
                rig.Stand = -footY / legs + 0.015f * size;
                rig.Stride = math.max(0.1f, 2 * travel / legs);
            }
            pos.Dispose(); rot.Dispose(); end.Dispose(); endRot.Dispose();
        }

        void Apply(Plan p)
        {
            var graphics = World.GetExistingSystemManaged<EntitiesGraphicsSystem>();
            int shapeCount = p.Shapes.Count;
            var meshIds = new BatchMeshID[2 * shapeCount];
            for (int s = 0; s < 2 * shapeCount; s++)
            {
                var vs = s < shapeCount ? p.Verts[s] : p.LowVerts[s - shapeCount];
                var ix = s < shapeCount ? p.Index[s] : p.LowIndex[s - shapeCount];
                var data = Mesh.AllocateWritableMeshData(1);
                var d = data[0];
                int vc = vs.Length / CreatureMesh.Stride;
                d.SetVertexBufferParams(vc, Layout);
                d.GetVertexData<float>().CopyFrom(vs);
                d.SetIndexBufferParams(ix.Length, IndexFormat.UInt16);
                d.GetIndexData<ushort>().CopyFrom(ix);
                d.subMeshCount = 1;
                d.SetSubMesh(0, new SubMeshDescriptor(0, ix.Length));
                var mesh = new Mesh { name = "Creature " + s };
                Mesh.ApplyAndDisposeWritableMeshData(data, mesh);
                mesh.bounds = mesh.GetSubMesh(0).bounds;
                meshes.Add(mesh);
                meshIds[s] = graphics.RegisterMesh(mesh);
            }
            var prefabs = new Entity[2 * shapeCount];
            for (int s = 0; s < 2 * shapeCount; s++)
            {
                var e = EntityManager.CreateEntity();
                RenderMeshUtility.AddComponents(e, EntityManager, new RenderMeshDescription(ShadowCastingMode.On, true, MotionVectorGenerationMode.ForceNoMotion, 0), new MaterialMeshInfo(materialId, meshIds[s]));
                EntityManager.AddComponentData(e, new ObjectFade { Value = new float4(1, 1, 0, 0.5f) });
                EntityManager.AddComponentData(e, new CreatureFurBase());
                EntityManager.AddComponentData(e, new CreatureFurAccent());
                EntityManager.AddComponentData(e, new CreatureFurDark());
                EntityManager.AddComponent<Prefab>(e);
                prefabs[s] = e;
            }
            foreach (var h in p.Herd)
            {
                int first = h.First;
                var rig = Rig(h.Def, h.Body);
                rigs.Add(rig);
                var root = EntityManager.CreateEntity();
                EntityManager.AddComponentData(root, new CreatureRoot
                {
                    Rig = rig, Position = new float3(h.At.x, 0, h.At.y), Home = h.At, Heading = h.Heading, WalkSpeed = h.Body.WalkSpeed,
                    Random = new Unity.Mathematics.Random(h.Seed), Timer = 0.5f + (h.Seed % 997) / 997f * 3, Visible = true, Shadow = true,
                    High = true, HighOn = true, LowOn = true,
                    Phase = (h.Seed % 389) / 389f, Clock = (h.Seed % 211) / 7f,
                });
                var links = EntityManager.AddBuffer<CreatureLink>(root);
                var fur = (b: Linear(h.Body.Base), a: Linear(h.Body.Accent), d: Linear(h.Body.Dark));
                var trunkShape = p.Shapes[first];
                for (int tier = 0; tier < 2; tier++)
                {
                    int at = first + tier * shapeCount;
                    var link = Link(prefabs[at], new float3(h.Body.Trunk.Radius, h.Body.Trunk.Radius, h.Body.Trunk.Length / math.max(trunkShape.Length, 1e-3f)), fur, h.Body.Length);
                    links = EntityManager.GetBuffer<CreatureLink>(root);
                    links.Add(link);
                    foreach (var seg in h.Body.Segments)
                    {
                        var shape = p.Shapes[first + seg.Shape];
                        float3 scale = shape.Sphere ? new float3(seg.Radius) : new float3(seg.Radius, seg.Radius, seg.Length / math.max(shape.Length, 1e-3f));
                        link = Link(prefabs[at + seg.Shape], scale, fur, h.Body.Length);
                        links = EntityManager.GetBuffer<CreatureLink>(root);
                        links.Add(link);
                    }
                }
                Count++;
            }
        }

        CreatureLink Link(Entity prefab, float3 scale, (float4 b, float4 a, float4 d) fur, float length)
        {
            var e = EntityManager.Instantiate(prefab);
            EntityManager.SetComponentData(e, new LocalToWorld { Value = float4x4.TRS(0, quaternion.identity, scale) });
            EntityManager.SetComponentData(e, new CreatureFurBase { Value = fur.b });
            EntityManager.SetComponentData(e, new CreatureFurAccent { Value = fur.a });
            EntityManager.SetComponentData(e, new CreatureFurDark { Value = fur.d });
            EntityManager.SetComponentData(e, new RenderBounds { Value = new Unity.Mathematics.AABB { Center = 0, Extents = new float3(1.2f * length) } });
            return new CreatureLink { Value = e, Scale = scale };
        }
    }
}
