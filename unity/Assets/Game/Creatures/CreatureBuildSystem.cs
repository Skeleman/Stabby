using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Stabby.Viewer;
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
            public BlobAssetReference<CreatureRig> Rig;
            public int[] Prefab;
            public float3[] Scale;
            public float4 Base, Accent, Dark;
            public float Length, Distance;
            public CreatureDef Def;
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
        public IReadOnlyList<Mesh> Meshes => meshes;

        Task<Kit> kitTask;
        Kit kit;
        Entity[] prefabs;
        Task<List<Arrival>> arriving;
        Material material;
        BatchMaterialID materialId;
        readonly List<Mesh> meshes = new List<Mesh>();
        readonly Dictionary<Entity, Shown> shown = new Dictionary<Entity, Shown>();
        readonly List<Entity> gone = new List<Entity>();
        readonly List<Arrival> queue = new List<Arrival>();
        readonly HashSet<Entity> queued = new HashSet<Entity>();
        EntityQuery ghosts;
        EntityArchetype rootType;
        bool counts, instanced;
        Entity tally;

        protected override void OnCreate()
        {
            counts = World.Name == GameSettings.ViewClient;
            instanced = CreaturePath.Instanced;
            if (counts) Count = 0;
            EntityManager.CreateSingleton(new CreaturePresenter { Index = Math.Max(0, Array.IndexOf(GameSettings.Clients, World.Name)) });
            tally = EntityManager.CreateSingleton(new CreatureTally { Instanced = instanced });
            ghosts = SystemAPI.QueryBuilder().WithAll<CreatureState>().Build();
            rootType = instanced
                ? EntityManager.CreateArchetype(typeof(CreatureRoot), typeof(CreatureLink), typeof(CreatureFur))
                : EntityManager.CreateArchetype(typeof(CreatureRoot), typeof(CreatureLink));
        }

        protected override void OnDestroy()
        {
            Owned.Release(kitTask, _ => { });
            Owned.Release(arriving, Drop);
            Drop(queue);
            queue.Clear();
            queued.Clear();
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
            if (materialId == BatchMaterialID.Null && !instanced)
            {
                materialId = World.GetExistingSystemManaged<EntitiesGraphicsSystem>().RegisterMaterial(material);
                return false;
            }
            return true;
        }

        static void Drop(List<Arrival> list)
        {
            foreach (var a in list) if (a.Rig.IsCreated) a.Rig.Dispose();
        }

        bool Alive(Entity ghost) => EntityManager.Exists(ghost) && EntityManager.HasComponent<CreatureState>(ghost);

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

            Vanish();

            if (arriving != null && arriving.IsCompleted)
            {
                if (arriving.IsFaulted) { Debug.LogException(arriving.Exception); Enabled = false; return; }
                queue.AddRange(arriving.Result);
                arriving = null;
            }

            if (queue.Count > 0) Arrive();
            if (counts) Count = shown.Count;
            if (arriving != null) return;

            var es = ghosts.ToEntityArray(Allocator.Temp);
            List<Arrival> fresh = null;
            foreach (var e in es)
            {
                if (shown.ContainsKey(e) || queued.Contains(e)) continue;
                var st = EntityManager.GetComponentData<CreatureState>(e);
                if (st.Body < 0 || st.Body >= kit.Lib.Bodies.Count) continue;
                fresh ??= new List<Arrival>();
                fresh.Add(new Arrival { Ghost = e, State = st });
                queued.Add(e);
            }
            es.Dispose();
            if (fresh == null) return;
            var k = kit;
            arriving = Task.Run(() =>
            {
                for (int i = 0; i < fresh.Count; i++) fresh[i] = Prepare(k, fresh[i]);
                return fresh;
            });
        }

        void Vanish()
        {
            gone.Clear();
            foreach (var kv in shown)
                if (!Alive(kv.Key)) gone.Add(kv.Key);
            if (gone.Count == 0) return;
            var doomed = new NativeList<Entity>(gone.Count * 32, Allocator.Temp);
            int dropped = 0;
            foreach (var g in gone)
            {
                var s = shown[g];
                var links = EntityManager.GetBuffer<CreatureLink>(s.Root);
                dropped += links.Length;
                for (int i = 0; i < links.Length; i++) if (links[i].Value != Entity.Null) doomed.Add(links[i].Value);
                doomed.Add(s.Root);
                s.Rig.Dispose();
                shown.Remove(g);
            }
            EntityManager.DestroyEntity(doomed.AsArray());
            doomed.Dispose();
            AddLinks(-dropped);
        }

        void AddLinks(int n)
        {
            var t = EntityManager.GetComponentData<CreatureTally>(tally);
            t.Links += n;
            EntityManager.SetComponentData(tally, t);
        }

        static Arrival Prepare(Kit k, Arrival a)
        {
            var def = k.Lib.Bodies[a.State.Body];
            var body = CreatureBody.Assemble(def, k.Lib, a.State.BodySeed);
            int first = k.First[a.State.Body], shapeCount = k.Shapes.Count, per = 1 + body.Segments.Count;
            a.Def = def;
            a.Body = body;
            a.Prefab = new int[2 * per];
            a.Scale = new float3[2 * per];
            var trunk = k.Shapes[first];
            for (int tier = 0; tier < 2; tier++)
            {
                int at = first + tier * shapeCount, o = tier * per;
                a.Prefab[o] = at;
                a.Scale[o] = new float3(body.Trunk.Radius, body.Trunk.Radius, body.Trunk.Length / math.max(trunk.Length, 1e-3f));
                for (int j = 0; j < body.Segments.Count; j++)
                {
                    var seg = body.Segments[j];
                    var shape = k.Shapes[first + seg.Shape];
                    a.Prefab[o + 1 + j] = at + seg.Shape;
                    a.Scale[o + 1 + j] = shape.Sphere ? new float3(seg.Radius) : new float3(seg.Radius, seg.Radius, seg.Length / math.max(shape.Length, 1e-3f));
                }
            }
            a.Base = Linear(body.Base);
            a.Accent = Linear(body.Accent);
            a.Dark = Linear(body.Dark);
            a.Length = body.Length;
            return a;
        }

        void Arrive()
        {
            float3 cam = DetailView.Current.Camera;
            for (int i = queue.Count - 1; i >= 0; i--)
            {
                var a = queue[i];
                if (shown.ContainsKey(a.Ghost) || !Alive(a.Ghost))
                {
                    if (a.Rig.IsCreated) a.Rig.Dispose();
                    queued.Remove(a.Ghost);
                    queue.RemoveAt(i);
                    continue;
                }
                var st = EntityManager.GetComponentData<CreatureState>(a.Ghost);
                a.State.Position = st.Position;
                a.State.Heading = st.Heading;
                a.Distance = math.distancesq(cam.xz, st.Position);
                queue[i] = a;
            }
            if (queue.Count == 0) return;
            queue.Sort((x, y) => x.Distance.CompareTo(y.Distance));

            var budget = new FrameBudget(FrameBudget.CreatureEntities);
            int m = 0, links = 0;
            while (m < queue.Count && budget.Take(1 + queue[m].Prefab.Length)) links += queue[m++].Prefab.Length;

            var perPrefab = new int[prefabs.Length];
            for (int i = 0; i < m; i++) foreach (int p in queue[i].Prefab) perPrefab[p]++;
            var made = new NativeArray<Entity>[prefabs.Length];
            for (int p = 0; p < prefabs.Length; p++)
                if (perPrefab[p] > 0 && !instanced) made[p] = EntityManager.Instantiate(prefabs[p], perPrefab[p], Allocator.Temp);
            var roots = EntityManager.CreateEntity(rootType, m, Allocator.Temp);

            var linkEntity = new NativeArray<Entity>(links, Allocator.TempJob);
            var linkScale = new NativeArray<float3>(links, Allocator.TempJob);
            var linkOwner = new NativeArray<int>(links, Allocator.TempJob);
            var fur = new NativeArray<float4>(3 * m, Allocator.TempJob);
            var size = new NativeArray<float>(m, Allocator.TempJob);
            Array.Clear(perPrefab, 0, perPrefab.Length);
            int at = 0;
            for (int i = 0; i < m; i++)
            {
                var a = queue[i];
                a.Rig = Rig(a.Def, a.Body);
                uint seed = a.State.Seed;
                EntityManager.SetComponentData(roots[i], new CreatureRoot
                {
                    Rig = a.Rig, Ghost = a.Ghost, Position = new float3(a.State.Position.x, 0, a.State.Position.y), Heading = a.State.Heading, WalkSpeed = a.State.WalkSpeed,
                    Visible = true, Shadow = true, High = true, HighOn = true, LowOn = true,
                    Phase = (seed % 389) / 389f, Clock = (seed % 211) / 7f,
                });
                var buffer = EntityManager.GetBuffer<CreatureLink>(roots[i]);
                buffer.ResizeUninitialized(a.Prefab.Length);
                if (instanced) EntityManager.SetComponentData(roots[i], new CreatureFur { Base = a.Base, Accent = a.Accent, Dark = a.Dark });
                for (int j = 0; j < a.Prefab.Length; j++)
                {
                    int p = a.Prefab[j];
                    var e = instanced ? Entity.Null : made[p][perPrefab[p]++];
                    buffer[j] = new CreatureLink { Value = e, Scale = a.Scale[j], Mesh = p };
                    linkEntity[at] = e;
                    linkScale[at] = a.Scale[j];
                    linkOwner[at] = i;
                    at++;
                }
                fur[3 * i] = a.Base;
                fur[3 * i + 1] = a.Accent;
                fur[3 * i + 2] = a.Dark;
                size[i] = a.Length;
                shown[a.Ghost] = new Shown { Root = roots[i], Rig = a.Rig };
                queued.Remove(a.Ghost);
            }
            queue.RemoveRange(0, m);
            foreach (var arr in made) if (arr.IsCreated) arr.Dispose();
            roots.Dispose();
            AddLinks(links);
            if (instanced)
            {
                linkEntity.Dispose();
                linkScale.Dispose();
                linkOwner.Dispose();
                fur.Dispose();
                size.Dispose();
                return;
            }

            Dependency = new Dress
            {
                Links = linkEntity, Scales = linkScale, Owner = linkOwner, Fur = fur, Size = size,
                Ltw = GetComponentLookup<LocalToWorld>(), Base = GetComponentLookup<CreatureFurBase>(), Accent = GetComponentLookup<CreatureFurAccent>(),
                Dark = GetComponentLookup<CreatureFurDark>(), Bounds = GetComponentLookup<RenderBounds>(),
            }.Schedule(links, 64, Dependency);
            linkEntity.Dispose(Dependency);
            linkScale.Dispose(Dependency);
            linkOwner.Dispose(Dependency);
            fur.Dispose(Dependency);
            size.Dispose(Dependency);
        }

        [BurstCompile]
        struct Dress : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Entity> Links;
            [ReadOnly] public NativeArray<float3> Scales;
            [ReadOnly] public NativeArray<int> Owner;
            [ReadOnly] public NativeArray<float4> Fur;
            [ReadOnly] public NativeArray<float> Size;
            [NativeDisableParallelForRestriction] public ComponentLookup<LocalToWorld> Ltw;
            [NativeDisableParallelForRestriction] public ComponentLookup<CreatureFurBase> Base;
            [NativeDisableParallelForRestriction] public ComponentLookup<CreatureFurAccent> Accent;
            [NativeDisableParallelForRestriction] public ComponentLookup<CreatureFurDark> Dark;
            [NativeDisableParallelForRestriction] public ComponentLookup<RenderBounds> Bounds;

            public void Execute(int i)
            {
                var e = Links[i];
                int o = Owner[i];
                Ltw[e] = new LocalToWorld { Value = float4x4.TRS(0, quaternion.identity, Scales[i]) };
                Base[e] = new CreatureFurBase { Value = Fur[3 * o] };
                Accent[e] = new CreatureFurAccent { Value = Fur[3 * o + 1] };
                Dark[e] = new CreatureFurDark { Value = Fur[3 * o + 2] };
                Bounds[e] = new RenderBounds { Value = new Unity.Mathematics.AABB { Center = 0, Extents = new float3(1.2f * Size[o]) } };
            }
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

        static BlobAssetReference<CreatureRig> Rig(CreatureDef def, CreatureBody body)
        {
            var b = new BlobBuilder(Allocator.Persistent);
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
            rig.Rule = def.Rule();
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
                    RunForward = V(s.RunReach), RunBack = V(s.RunPush), RunLift = V(s.RunLift), Alert = V(s.Alert), Graze = V(s.Graze),
                };
            }
            b.Allocate(ref rig.RestPos, body.Segments.Count);
            b.Allocate(ref rig.RestRot, body.Segments.Count);
            var blob = b.CreateBlobAssetReference<CreatureRig>(Allocator.Persistent);
            b.Dispose();
            Measure(ref blob.Value, body.Size);
            return blob;
        }

        static void Measure(ref CreatureRig rig, float size)
        {
            int n = rig.Segments.Length, parts = rig.Parts.Length;
            var pos = new NativeArray<float3>(n, Allocator.Persistent);
            var rot = new NativeArray<quaternion>(n, Allocator.Persistent);
            var end = new NativeArray<float3>(parts, Allocator.Persistent);
            var endRot = new NativeArray<quaternion>(parts, Allocator.Persistent);
            CreaturePose.Solve(ref rig, 0, quaternion.identity, 0, 0, 0, 0, 0, 0, 0, pos, rot, end, endRot);
            for (int i = 0; i < n; i++)
            {
                rig.RestPos[i] = pos[i];
                rig.RestRot[i] = rot[i];
            }
            CreaturePose.Solve(ref rig, 0, quaternion.identity, 0, 0, 0, 0, 0, 0, 1, pos, rot, end, endRot);
            float footY = 0, travel = 0, fore = 0, hind = 0, track = 0;
            int legs = 0, fores = 0;
            for (int i = 0; i < parts; i++)
            {
                if (rig.Parts[i].Role != CreaturePartDef.Leg) continue;
                rig.Parts[i].Foot = end[i];
                footY += end[i].y;
                track += math.abs(end[i].x);
                if (end[i].z > 0) { fore += end[i].z; fores++; }
                else hind += end[i].z;
                legs++;
            }
            bool paired = fores > 0 && fores < legs;
            rig.Fore = paired ? fore / fores : 0.5f * rig.Length;
            rig.Hind = paired ? hind / (legs - fores) : -0.5f * rig.Length;
            rig.Track = legs > 0 ? track / legs : 0;
            for (int i = 0; i < parts; i++)
            {
                ref var part = ref rig.Parts[i];
                if (part.Role != CreaturePartDef.Leg) continue;
                CreaturePose.Solve(ref rig, 0, quaternion.identity, math.frac(-part.Phase), 1, 0, 0, 0, 0, 1, pos, rot, end, endRot);
                float front = end[i].z;
                CreaturePose.Solve(ref rig, 0, quaternion.identity, math.frac(0.5f - part.Phase), 1, 0, 0, 0, 0, 1, pos, rot, end, endRot);
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
                if (!instanced) meshIds[s] = graphics.RegisterMesh(mesh);
            }
            prefabs = new Entity[2 * shapeCount];
            EntityManager.SetComponentData(tally, new CreatureTally { Instanced = instanced, Groups = 4 * shapeCount });
            if (instanced) return;
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
    }
}
