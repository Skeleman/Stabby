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
        const float Scale = 1.5f;

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

        void Load()
        {
            var graphics = World.GetExistingSystemManaged<EntitiesGraphicsSystem>();
            var prim = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            var src = prim.GetComponent<MeshFilter>().sharedMesh;
            var verts = src.vertices;
            for (int k = 0; k < verts.Length; k++) verts[k] = verts[k] * Scale + Vector3.up * Scale;
            mesh = new Mesh { name = "Player capsule", vertices = verts, normals = src.normals, triangles = src.triangles };
            mesh.RecalculateBounds();
            Object.Destroy(prim);
            bounds = new AABB { Center = mesh.bounds.center, Extents = mesh.bounds.extents };
            meshId = graphics.RegisterMesh(mesh);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            materials = new Material[Colors.Length];
            materialIds = new BatchMaterialID[Colors.Length];
            for (int k = 0; k < Colors.Length; k++)
            {
                materials[k] = new Material(shader) { color = Colors[k] };
                materialIds[k] = graphics.RegisterMaterial(materials[k]);
            }
        }

        protected override void OnUpdate()
        {
            Frames();
            if (!unshown.IsEmpty)
            {
                if (mesh == null) Load();
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
            foreach (var (t, owner) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<GhostOwner>>().WithAll<Player>())
            {
                if (owner.ValueRO.NetworkId != me.Value) continue;
                Vector3 p = (Vector3)t.ValueRO.Position + Vector3.up * Scale;
                cam.transform.position = p + new Vector3(0, 10, -22);
                cam.transform.LookAt(p + Vector3.up * 2);
            }
        }

        int frames, slow;
        float worst, worstPlay;
        string worstWhy;

        void Frames()
        {
            if (++frames <= 10) return;
            float ms = UnityEngine.Time.unscaledDeltaTime * 1000f;
            if (UnityEngine.Time.frameCount - GameStatus.ToolFrame <= 1) GameStatus.ToolFrames++;
            else
            {
                if (ms > worst) { worst = ms; worstWhy = $"frame {frames - 10}, {GameStatus.TerrainFrame}, gc {System.GC.CollectionCount(0)}"; }
                if (ms > 33f) slow++;
                if (GameStatus.TerrainBuilt && ms > worstPlay) worstPlay = ms;
            }
            GameStatus.Lines["~frames"] = $"frames {frames - 10}, worst {worst:F0} ms ({worstWhy}), {slow} over 33 ms, worst after terrain built {worstPlay:F0} ms, {GameStatus.ToolFrames} capture frames left out";
        }
    }
}
