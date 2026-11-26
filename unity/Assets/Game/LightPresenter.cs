using System.Collections.Generic;
using System.Threading.Tasks;
using Stabby.Viewer;
using Stabby.World;
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
        static readonly int GlowsId = Shader.PropertyToID("_Glows");
        Task<LightBudget.Caps> capsTask;
        readonly List<Light> pool = new List<Light>();
        GraphicsBuffer glows;
        Material material;
        SystemHandle budgetSystem;
        int uploaded = -1, used;
        public int Real, Shadowed, Glows, Cores;
        public bool Fading;

        protected override void OnCreate()
        {
            budgetSystem = World.GetOrCreateSystem<LightBudgetSystem>();
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
            if (!SystemAPI.TryGetSingletonRW<LightBudgetState>(out var budget)) { Hide(); return; }
            budget.ValueRW.Clock = (float)(DayNightSystem.Clock % 3600.0);
            budget.ValueRW.Daylight = Mathf.Clamp01(DayNightSystem.Shown.Intensity);
            if (budget.ValueRO.Hidden) { Hide(); return; }
            ref var b = ref World.Unmanaged.GetUnsafeSystemRef<LightBudgetSystem>(budgetSystem);
            var r = b.ShownResult.Value;
            Real = r.Real;
            Shadowed = r.Shadowed;
            Cores = r.Cores;
            Glows = r.Glows;
            Fading = r.Fading;
            var lit = b.Shown;
            for (int i = 0; i < lit.Length; i++)
            {
                var e = lit[i];
                var s = e.Slot;
                var l = Take(i);
                l.transform.position = e.Position;
                l.color = new Color(e.Color.x, e.Color.y, e.Color.z);
                var type = e.ConeDeg > 0f ? LightType.Spot : LightType.Point;
                if (l.type != type) l.type = type;
                if (type == LightType.Spot)
                {
                    l.spotAngle = e.ConeDeg;
                    l.innerSpotAngle = e.ConeDeg * 0.4f;
                    l.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                }
                l.range = e.RangeM;
                l.intensity = e.Intensity * e.Gain(r.Clock) * s.RealWeight;
                l.shadows = s.Shadow > 0f ? LightShadows.Soft : LightShadows.None;
                l.shadowStrength = s.ShadowWeight;
            }
            Release(lit.Length);
            int count = b.ShownGlows.Length;
            if (count == 0) return;
            if (!Ready(count)) return;
            if (uploaded != b.Published)
            {
                glows.SetData(b.ShownGlows.AsArray(), 0, 0, count);
                uploaded = b.Published;
            }
            var rp = new RenderParams(material)
            {
                worldBounds = new Bounds(Vector3.zero, Vector3.one * 1e6f),
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                motionVectorMode = MotionVectorGenerationMode.ForceNoMotion,
            };
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, 6, count);
        }

        void Hide()
        {
            Real = Shadowed = Glows = Cores = 0;
            Fading = false;
            Release(0);
        }

        void Release(int keep)
        {
            for (int i = keep; i < used; i++) if (pool[i].enabled) pool[i].enabled = false;
            used = keep;
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

        bool Ready(int count)
        {
            if (material == null)
            {
                var shader = Shader.Find("Stabby/Glow");
                if (shader == null) return false;
                material = new Material(shader) { name = "Stabby light glow" };
            }
            if (glows == null || glows.count < count)
            {
                int old = glows == null ? 32 : glows.count;
                glows?.Release();
                glows = new GraphicsBuffer(GraphicsBuffer.Target.Structured, math.max(count, old * 2), 32);
                material.SetBuffer(GlowsId, glows);
                uploaded = -1;
            }
            return true;
        }
    }
}
