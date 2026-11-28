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
    public partial class ClientViewSystem : SystemBase
    {
        static readonly Color[] Colors = { new Color(0.85f, 0.25f, 0.2f), new Color(0.2f, 0.45f, 0.9f), new Color(0.95f, 0.8f, 0.2f), new Color(0.3f, 0.75f, 0.35f) };
        const float Scale = 0.9f;

        Mesh mesh;
        Material[] materials;
        BatchMeshID meshId;
        BatchMaterialID[] materialIds;
        AABB bounds;
        EntityQuery unshown, shown;

        protected override void OnCreate()
        {
            if (World.Name != GameSettings.ViewClient) { Enabled = false; return; }
            unshown = SystemAPI.QueryBuilder().WithAll<Player, GhostOwner, LocalTransform>().WithNone<MaterialMeshInfo>().Build();
            shown = SystemAPI.QueryBuilder().WithAll<Player, MaterialMeshInfo>().Build();
        }

        protected override void OnDestroy()
        {
            if (mesh != null) Object.Destroy(mesh);
            if (materials != null) foreach (var m in materials) Object.Destroy(m);
        }

        Shader shader;
        int warmed;

        bool Warm()
        {
            int n = Colors.Length;
            if (warmed > 2 + 2 * n) return true;
            if (frames <= 10) return false;
            var graphics = World.GetExistingSystemManaged<EntitiesGraphicsSystem>();
            if (warmed == 0)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit");
                materials = new Material[n];
                materialIds = new BatchMaterialID[n];
            }
            else if (warmed == 1) LoadMesh(graphics);
            else if (warmed == 2 + 2 * n)
            {
                if (!TerrainLoading()) return false;
                WarmEntity();
            }
            else
            {
                int k = (warmed - 2) / 2;
                if ((warmed & 1) == 0) materials[k] = new Material(shader) { color = Colors[k] };
                else materialIds[k] = graphics.RegisterMaterial(materials[k]);
            }
            warmed++;
            return false;
        }

        void LoadMesh(EntitiesGraphicsSystem graphics)
        {
            var prim = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            var src = prim.GetComponent<MeshFilter>().sharedMesh;
            var verts = src.vertices;
            for (int k = 0; k < verts.Length; k++) verts[k] = verts[k] * Scale + Vector3.up * Scale;
            mesh = new Mesh { name = "Player capsule", vertices = verts, normals = src.normals, triangles = src.triangles };
            mesh.RecalculateBounds();
            Object.Destroy(prim);
            bounds = new AABB { Center = mesh.bounds.center, Extents = mesh.bounds.extents };
            meshId = graphics.RegisterMesh(mesh);
        }

        bool TerrainLoading()
        {
            var terrain = World.GetExistingSystemManaged<TerrainViewSystem>();
            return terrain == null || !terrain.Enabled || terrain.Loading;
        }

        void WarmEntity()
        {
            var e = EntityManager.CreateEntity(typeof(LocalTransform), typeof(LocalToWorld));
            EntityManager.SetComponentData(e, LocalTransform.FromPosition(new float3(0, -10000, 0)));
            EntityManager.SetComponentData(e, new LocalToWorld { Value = float4x4.Translate(new float3(0, -10000, 0)) });
            RenderMeshUtility.AddComponents(e, EntityManager, new RenderMeshDescription(ShadowCastingMode.On), new MaterialMeshInfo(materialIds[0], meshId));
            EntityManager.SetComponentData(e, new RenderBounds { Value = bounds });
        }

        protected override void OnUpdate()
        {
            Frames();
            if (Warm() && !unshown.IsEmpty)
            {
                var es = unshown.ToEntityArray(Allocator.Temp);
                var desc = new RenderMeshDescription(ShadowCastingMode.On);
                foreach (var e in es)
                {
                    int id = EntityManager.GetComponentData<GhostOwner>(e).NetworkId;
                    RenderMeshUtility.AddComponents(e, EntityManager, desc, new MaterialMeshInfo(materialIds[id % Colors.Length], meshId));
                    EntityManager.SetComponentData(e, new RenderBounds { Value = bounds });
                }
            }
            GameStatus.Capsules = shown.CalculateEntityCount();

            var cam = Camera.main;
            if (cam == null || GameStatus.HoldCamera || !SystemAPI.TryGetSingleton<NetworkId>(out var me)) return;
            bool hasGround = SystemAPI.TryGetSingleton<Ground>(out var ground);
            var cols = SystemAPI.TryGetSingleton<ObjectColumns>(out var oc) ? oc : default;
            foreach (var (t, owner) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<GhostOwner>>().WithAll<Player>())
            {
                if (owner.ValueRO.NetworkId != me.Value) continue;
                var pivot = t.ValueRO.Position + new float3(0, PlayerControls.Lift, 0);
                var back = PlayerControls.Back;
                float boom = PlayerControls.EaseBoom(hasGround ? PlayerControls.BoomClear(ground, cols, pivot, back) : PlayerControls.Boom, UnityEngine.Time.unscaledDeltaTime);
                var eye = pivot + back * boom;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(-back));
            }
        }

        int frames;

        void Frames()
        {
            if (++frames <= 10) return;
            GameStatus.Frames = frames - 10;
            float ms = UnityEngine.Time.unscaledDeltaTime * 1000f;
            if (GameStatus.AfterTool) GameStatus.ToolFrames++;
            else
            {
                if (ms > GameStatus.WorstMs) { GameStatus.WorstMs = ms; GameStatus.WorstWhy = $"frame {frames - 10}, terrain {GameStatus.TerrainMs:F1} ms {GameStatus.TerrainStage}, {GameStatus.TerrainVertices} vertices, gc {System.GC.CollectionCount(0)}"; }
                if (ms > 33f) GameStatus.SlowFrames++;
                if (GameStatus.TerrainBuilt && ms > GameStatus.WorstPlayMs) GameStatus.WorstPlayMs = ms;
            }
        }
    }
}
