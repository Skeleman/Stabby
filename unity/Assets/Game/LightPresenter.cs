using System.Collections.Generic;
using System.Threading.Tasks;
using Stabby.Viewer;
using Stabby.World;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Stabby.Game
{
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(LightBudgetSystem))]
    public partial class LightPresenterSystem : SystemBase
    {
        public const float CoreM = 0.09f, CoreGain = 2f, HaloShare = 0.18f, HaloGain = 0.06f;
        static readonly float3 Luma = new float3(0.2126f, 0.7152f, 0.0722f);
        static readonly int GlowsId = Shader.PropertyToID("_Glows");
        Task<LightBudget.Caps> capsTask;
        readonly List<Light> pool = new List<Light>();
        struct GlowItem { public Vector4 PosSize, Color; }
        GlowItem[] glowData = new GlowItem[0];
        GraphicsBuffer glows;
        Material material;
        EntityQuery query;
        public int Real, Shadowed, Glows, Cores;
        public bool Fading;

        protected override void OnCreate()
        {
            query = GetEntityQuery(ComponentType.ReadOnly<LightEmitter>());
            capsTask = Task.Run(() => ContentFiles.Objects(ContentFiles.Biomes()).LightCaps);
        }

        protected override void OnDestroy()
        {
            glows?.Release();
            if (material != null) Object.Destroy(material);
            foreach (var l in pool) if (l != null) Object.Destroy(l.gameObject);
            pool.Clear();
        }

        protected override void OnUpdate()
        {
            if (capsTask != null && capsTask.IsCompleted)
            {
                if (capsTask.IsFaulted) GameStatus.Failure = capsTask.Exception.ToString();
                else if (!SystemAPI.HasSingleton<LightBudgetState>()) EntityManager.CreateSingleton(new LightBudgetState { Caps = capsTask.Result, Budgeted = true });
                capsTask = null;
            }
            Real = Shadowed = Glows = Cores = 0;
            Fading = false;
            bool hidden = !SystemAPI.TryGetSingleton<LightBudgetState>(out var budget) || budget.Hidden;
            var items = query.ToComponentDataArray<LightEmitter>(Allocator.Temp);
            int need = hidden ? 0 : items.Length;
            if (glowData.Length < need * 2) glowData = new GlowItem[math.max(need * 2, 64)];
            int used = 0;
            for (int i = 0; i < need; i++)
            {
                var e = items[i];
                var s = e.Slot;
                if ((s.Shadow > 0f && s.Shadow < 1f) || (s.Real > 0f && s.Real < 1f) || (s.Glow > 0f && s.Glow < 1f)) Fading = true;
                var color = new Vector4(e.Color.x, e.Color.y, e.Color.z, 0f);
                if (s.Real > 0f)
                {
                    var l = Take(used++);
                    l.transform.position = e.Position;
                    l.color = new Color(e.Color.x, e.Color.y, e.Color.z);
                    l.range = e.RangeM;
                    l.intensity = e.Intensity * s.RealWeight;
                    l.shadows = s.Shadow > 0f ? LightShadows.Soft : LightShadows.None;
                    l.shadowStrength = s.ShadowWeight;
                    Real++;
                    if (s.Shadow > 0f) Shadowed++;
                }
                var p = new Vector4(e.Position.x, e.Position.y, e.Position.z, CoreM);
                glowData[Cores + Glows] = new GlowItem { PosSize = p, Color = color + new Vector4(0, 0, 0, CoreGain * e.Intensity) };
                Cores++;
                if (s.Glow > 0f)
                {
                    glowData[Cores + Glows] = new GlowItem { PosSize = new Vector4(p.x, p.y, p.z, e.RangeM * HaloShare), Color = HaloTint(e.Color) + new Vector4(0, 0, 0, HaloGain * e.Intensity * s.GlowWeight) };
                    Glows++;
                }
            }
            for (int i = used; i < pool.Count; i++) if (pool[i].enabled) pool[i].enabled = false;
            Draw(Cores + Glows);
        }

        static Vector4 HaloTint(float3 c)
        {
            var deep = c * c;
            float luma = math.dot(c, Luma), deepLuma = math.dot(deep, Luma);
            deep *= deepLuma > 1e-5f ? luma / deepLuma : 0f;
            return new Vector4(deep.x, deep.y, deep.z, 0f);
        }

        Light Take(int i)
        {
            while (pool.Count <= i)
            {
                var go = new GameObject("Stabby light " + pool.Count) { hideFlags = HideFlags.DontSave };
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.shadowBias = 0.05f;
                l.shadowNormalBias = 0.4f;
                pool.Add(l);
            }
            var light = pool[i];
            if (!light.enabled) light.enabled = true;
            return light;
        }

        void Draw(int count)
        {
            if (count == 0) return;
            if (material == null)
            {
                var shader = Shader.Find("Stabby/Glow");
                if (shader == null) return;
                material = new Material(shader) { name = "Stabby light glow" };
            }
            if (glows == null || glows.count < count)
            {
                int old = glows == null ? 32 : glows.count;
                glows?.Release();
                glows = new GraphicsBuffer(GraphicsBuffer.Target.Structured, math.max(count, old * 2), 32);
                material.SetBuffer(GlowsId, glows);
            }
            glows.SetData(glowData, 0, 0, count);
            var rp = new RenderParams(material)
            {
                worldBounds = new Bounds(Vector3.zero, Vector3.one * 1e6f),
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                motionVectorMode = MotionVectorGenerationMode.ForceNoMotion,
            };
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, 6, count);
        }
    }
}
