using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Stabby.Viewer;
using Stabby.World;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Stabby.Game
{
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateBefore(typeof(CreatureAnimSystem))]
    public partial class CreatureBuildSystem : SystemBase
    {
        sealed class Kit
        {
            public CreatureLibrary Lib;
            public List<CreatureShape> Shapes = new List<CreatureShape>();
            public List<float[]> Verts = new List<float[]>();
            public List<ushort[]> Index = new List<ushort[]>();
            public List<float[]> LowVerts = new List<float[]>();
            public List<ushort[]> LowIndex = new List<ushort[]>();
            public int[] First;
        }

        struct Arrival
        {
            public Entity Ghost;
            public CreatureState State;
            public CreatureBody Body;
        }

        struct Shown
        {
            public Entity Root;
            public BlobAssetReference<CreatureRig> Rig;
        }

        static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
        };

        public static int Count { get; private set; }

        Task<Kit> kitTask;
        Kit kit;
        Entity[] prefabs;
        Task<List<Arrival>> arriving;
        Material material;
        BatchMaterialID materialId;
        readonly List<Mesh> meshes = new List<Mesh>();
        readonly Dictionary<Entity, Shown> shown = new Dictionary<Entity, Shown>();
        readonly List<Entity> gone = new List<Entity>();
        EntityQuery ghosts;
        bool counts;

        protected override void OnCreate()
        {
            counts = World.Name == GameSettings.ViewClient;
            if (counts) Count = 0;
            EntityManager.CreateSingleton(new CreaturePresenter { Index = Math.Max(0, Array.IndexOf(GameSettings.Clients, World.Name)) });
            ghosts = SystemAPI.QueryBuilder().WithAll<CreatureState>().Build();
        }

        protected override void OnDestroy()
        {
            Owned.Release(kitTask, _ => { });
            Owned.Release(arriving, _ => { });
            foreach (var s in shown.Values) if (s.Rig.IsCreated) s.Rig.Dispose();
            shown.Clear();
            foreach (var m in meshes) if (m != null) Object.Destroy(m);
            meshes.Clear();
            if (material != null) Object.Destroy(material);
            if (counts) Count = 0;
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
            if (kit == null)
            {
                if (kitTask == null)
                {
                    var defs = World.GetExistingSystemManaged<ClientMapSystem>()?.Defs;
                    if (defs == null) return;
                    kitTask = Task.Run(() => MakeKit(defs));
                    return;
                }
                if (!kitTask.IsCompleted || !Warm()) return;
                if (kitTask.IsFaulted) { Debug.LogException(kitTask.Exception); Enabled = false; return; }
                kit = kitTask.Result;
                kitTask = null;
                MakePrefabs();
                return;
            }

            gone.Clear();
            foreach (var kv in shown)
                if (!EntityManager.Exists(kv.Key) || !EntityManager.HasComponent<CreatureState>(kv.Key)) gone.Add(kv.Key);
            foreach (var g in gone)
            {
                var s = shown[g];
                var links = EntityManager.GetBuffer<CreatureLink>(s.Root).ToNativeArray(Allocator.Temp);
                foreach (var link in links) EntityManager.DestroyEntity(link.Value);
                links.Dispose();
                EntityManager.DestroyEntity(s.Root);
                s.Rig.Dispose();
                shown.Remove(g);
            }

            if (arriving != null)
            {
                if (!arriving.IsCompleted) { if (counts) Count = shown.Count; return; }
                if (arriving.IsFaulted) { Debug.LogException(arriving.Exception); Enabled = false; return; }
                foreach (var a in arriving.Result)
                    if (a.Body != null && !shown.ContainsKey(a.Ghost) && EntityManager.Exists(a.Ghost) && EntityManager.HasComponent<CreatureState>(a.Ghost)) Build(a);
                arriving = null;
            }

            if (counts) Count = shown.Count;
            var es = ghosts.ToEntityArray(Allocator.Temp);
            List<Arrival> fresh = null;
            foreach (var e in es)
            {
                if (shown.ContainsKey(e)) continue;
                fresh ??= new List<Arrival>();
                fresh.Add(new Arrival { Ghost = e, State = EntityManager.GetComponentData<CreatureState>(e) });
            }
            es.Dispose();
            if (fresh == null) return;
            var lib = kit.Lib;
            arriving = Task.Run(() =>
            {
                for (int i = 0; i < fresh.Count; i++)
                {
                    var a = fresh[i];
                    if (a.State.Body < 0 || a.State.Body >= lib.Bodies.Count) continue;
                    a.Body = CreatureBody.Assemble(lib.Bodies[a.State.Body], lib, a.State.BodySeed);
                    fresh[i] = a;
                }
                return fresh;
            });
        }

        static Kit MakeKit(List<BiomeDef> defs)
        {
            var k = new Kit { Lib = ContentFiles.Creatures(defs) };
            k.First = new int[k.Lib.Bodies.Count];
            var verts = new List<float>();
            var index = new List<ushort>();
            for (int b = 0; b < k.Lib.Bodies.Count; b++)
            {
                k.First[b] = k.Shapes.Count;
                var shapes = CreatureBody.Shapes(k.Lib.Bodies[b], k.Lib);
                for (int s = 0; s < shapes.Count; s++)
                {
                    CreatureMesh.Build(shapes[s], s == 0, false, verts, index);
                    k.Shapes.Add(shapes[s]);
                    k.Verts.Add(verts.ToArray());
                    k.Index.Add(index.ToArray());
                    CreatureMesh.Build(shapes[s], s == 0, true, verts, index);
                    k.LowVerts.Add(verts.ToArray());
                    k.LowIndex.Add(index.ToArray());
                }
            }
            return k;
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
            rig.RunStride = def.RunStride * body.Size;
            rig.RunRatio = def.RunSpeed / def.WalkSpeed;
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
                    Phase = p.Phase, RunPhase = p.RunPhase, Side = p.Side, Reach = reach,
                };
            }
            var segs = b.Allocate(ref rig.Segments, body.Segments.Count);
            for (int i = 0; i < body.Segments.Count; i++)
            {
                var s = body.Segments[i];
                segs[i] = new CreatureSegmentRig
                {
                    Length = s.Length, Idle = V(s.Idle), Forward = V(s.Reach), Back = V(s.Push), Lift = V(s.Lift),
                    RunForward = V(s.RunReach), RunBack = V(s.RunPush), RunLift = V(s.RunLift), Alert = V(s.Alert),
                };
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
            CreaturePose.Solve(ref rig, 0, quaternion.identity, 0, 0, 0, 0, 0, pos, rot, end, endRot);
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
                CreaturePose.Solve(ref rig, 0, quaternion.identity, math.frac(-part.Phase), 1, 0, 0, 0, pos, rot, end, endRot);
                float front = end[i].z;
                CreaturePose.Solve(ref rig, 0, quaternion.identity, math.frac(0.5f - part.Phase), 1, 0, 0, 0, pos, rot, end, endRot);
                travel += front - end[i].z;
            }
            if (legs > 0)
            {
                rig.Stand = -footY / legs + 0.015f * size;
                rig.Stride = math.max(0.1f, 2 * travel / legs);
            }
            pos.Dispose(); rot.Dispose(); end.Dispose(); endRot.Dispose();
        }

        void MakePrefabs()
        {
            var graphics = World.GetExistingSystemManaged<EntitiesGraphicsSystem>();
            int shapeCount = kit.Shapes.Count;
            var meshIds = new BatchMeshID[2 * shapeCount];
            for (int s = 0; s < 2 * shapeCount; s++)
            {
                var vs = s < shapeCount ? kit.Verts[s] : kit.LowVerts[s - shapeCount];
                var ix = s < shapeCount ? kit.Index[s] : kit.LowIndex[s - shapeCount];
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
            prefabs = new Entity[2 * shapeCount];
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
        }

        void Build(Arrival a)
        {
            var def = kit.Lib.Bodies[a.State.Body];
            var body = a.Body;
            int first = kit.First[a.State.Body], shapeCount = kit.Shapes.Count;
            uint seed = a.State.Seed;
            var rig = Rig(def, body);
            var root = EntityManager.CreateEntity();
            shown[a.Ghost] = new Shown { Root = root, Rig = rig };
            EntityManager.AddComponentData(root, new CreatureRoot
            {
                Rig = rig, Ghost = a.Ghost, Position = new float3(a.State.Position.x, 0, a.State.Position.y), Heading = a.State.Heading, WalkSpeed = a.State.WalkSpeed,
                Visible = true, Shadow = true, High = true, HighOn = true, LowOn = true,
                Phase = (seed % 389) / 389f, Clock = (seed % 211) / 7f,
            });
            EntityManager.AddBuffer<CreatureLink>(root);
            var fur = (b: Linear(body.Base), a: Linear(body.Accent), d: Linear(body.Dark));
            var trunkShape = kit.Shapes[first];
            for (int tier = 0; tier < 2; tier++)
            {
                int at = first + tier * shapeCount;
                var link = Link(prefabs[at], new float3(body.Trunk.Radius, body.Trunk.Radius, body.Trunk.Length / math.max(trunkShape.Length, 1e-3f)), fur, body.Length);
                EntityManager.GetBuffer<CreatureLink>(root).Add(link);
                foreach (var seg in body.Segments)
                {
                    var shape = kit.Shapes[first + seg.Shape];
                    float3 scale = shape.Sphere ? new float3(seg.Radius) : new float3(seg.Radius, seg.Radius, seg.Length / math.max(shape.Length, 1e-3f));
                    link = Link(prefabs[at + seg.Shape], scale, fur, body.Length);
                    EntityManager.GetBuffer<CreatureLink>(root).Add(link);
                }
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
