using System.Threading.Tasks;
using Stabby.Viewer;
using Stabby.World;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Stabby.Game
{
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateBefore(typeof(TerrainViewSystem))]
    [UpdateBefore(typeof(ObjectViewSystem))]
    public partial class ShadowViewSystem : SystemBase
    {
        public const float SunStepSeconds = 0.1f;
        public static Quaternion? SunTarget;

        Task<(Detail.ShadowSpan, float)> spanTask;
        Detail.ShadowSpan unit;
        float texels;
        public static Detail.ShadowMap Map;
        public static float TexelScale = 1f;
        static readonly int SplitId = Shader.PropertyToID("_StabbyShadowSplit"), TexelId = Shader.PropertyToID("_StabbyShadowTexelM");
        bool ready;
        Light sun;
        float sunAt = float.MinValue;
        public float Range, Near;
        public Detail.Cascades Splits;

        protected override void OnCreate()
        {
            SunTarget = null;
            Map = default;
            spanTask = Task.Run(() =>
            {
                var biomes = ContentFiles.Biomes();
                var span = new Detail.ShadowSpan();
                var objects = ContentFiles.Objects(biomes);
                objects.AddShadows(ref span, Detail.ReferencePixelsPerMetre, 1f);
                ContentFiles.Creatures(biomes).AddShadows(ref span, Detail.ReferencePixelsPerMetre, 1f);
                return (span, objects.Terrain.ShadowTexels);
            });
        }

        protected override void OnUpdate()
        {
            StepSun();
            if (!ready)
            {
                if (spanTask == null || !spanTask.IsCompleted) return;
                if (spanTask.IsFaulted) { GameStatus.Failure = spanTask.Exception.ToString(); spanTask = null; Enabled = false; return; }
                (unit, texels) = spanTask.Result;
                spanTask = null;
                ready = true;
            }
            var cam = Camera.main;
            var asset = UniversalRenderPipeline.asset;
            if (cam == null || asset == null) return;
            var view = DetailView.Refresh(cam);
            float scale = view.PixelsPerMetre / Detail.ReferencePixelsPerMetre * view.Quality;
            float range = Detail.SnapRange(unit.Range * scale, Range);
            if (range != Range)
            {
                Range = range;
                Near = unit.Near * scale;
                Splits = Detail.CascadeSplits(Near, Range);
                asset.shadowDistance = Range;
                asset.shadowCascadeCount = Splits.Count;
                if (Splits.Count == 2) asset.cascade2Split = Splits.Split1;
                else if (Splits.Count == 3) asset.cascade3Split = new Vector2(Splits.Split1, Splits.Split2);
                else asset.cascade4Split = new Vector3(Splits.Split1, Splits.Split2, Splits.Split3);
            }
            Map = Build(asset, cam, view.PixelsPerMetre, texels * TexelScale);
            DetailView.SetShadows(Map);
            Shader.SetGlobalVector(SplitId, new Vector4(Map.Far0, Map.Far1, Map.Far2, Map.Range));
            Shader.SetGlobalVector(TexelId, new Vector4(Map.Texel0, Map.Texel1, Map.Texel2, Map.Texel3) * (Map.Texels / view.Quality));
        }

        static Detail.ShadowMap Build(UniversalRenderPipelineAsset asset, Camera cam, float ppm, float texels)
        {
            int count = asset.shadowCascadeCount;
            var c = new Detail.Cascades { Count = count };
            if (count == 2) c.Split1 = asset.cascade2Split;
            else if (count == 3) { c.Split1 = asset.cascade3Split.x; c.Split2 = asset.cascade3Split.y; }
            else if (count == 4) { c.Split1 = asset.cascade4Split.x; c.Split2 = asset.cascade4Split.y; c.Split3 = asset.cascade4Split.z; }
            int res = asset.mainLightShadowmapResolution;
            return Detail.ShadowMap.Build(c, asset.shadowDistance, count > 1 ? res / 2 : res, ppm, cam.aspect, texels);
        }

        void StepSun()
        {
            if (sun == null)
            {
                sun = RenderSettings.sun != null ? RenderSettings.sun : System.Array.Find(Object.FindObjectsByType<Light>(), l => l.type == LightType.Directional);
                if (sun == null) return;
                if (SunTarget == null) SunTarget = sun.transform.rotation;
            }
            float now = UnityEngine.Time.time;
            if (now - sunAt < SunStepSeconds) return;
            sunAt = math.floor(now / SunStepSeconds) * SunStepSeconds;
            if (SunTarget.HasValue && sun.transform.rotation != SunTarget.Value) sun.transform.rotation = SunTarget.Value;
        }
    }
}
