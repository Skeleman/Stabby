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

        Task<Detail.ShadowSpan> spanTask;
        Detail.ShadowSpan unit;
        bool ready;
        Light sun;
        float sunAt = float.MinValue;
        public float Range, Near;
        public Detail.Cascades Splits;

        protected override void OnCreate()
        {
            SunTarget = null;
            spanTask = Task.Run(() =>
            {
                var biomes = ContentFiles.Biomes();
                var span = new Detail.ShadowSpan();
                ContentFiles.Objects(biomes).AddShadows(ref span, Detail.ReferencePixelsPerMetre, 1f);
                ContentFiles.Creatures(biomes).AddShadows(ref span, Detail.ReferencePixelsPerMetre, 1f);
                return span;
            });
        }

        protected override void OnUpdate()
        {
            StepSun();
            if (!ready)
            {
                if (spanTask == null || !spanTask.IsCompleted) return;
                if (spanTask.IsFaulted) { GameStatus.Failure = spanTask.Exception.ToString(); spanTask = null; Enabled = false; return; }
                unit = spanTask.Result;
                spanTask = null;
                ready = true;
            }
            var cam = Camera.main;
            var asset = UniversalRenderPipeline.asset;
            if (cam == null || asset == null) return;
            var view = DetailView.Refresh(cam);
            float scale = view.PixelsPerMetre / Detail.ReferencePixelsPerMetre * view.Quality;
            float range = Detail.SnapRange(unit.Range * scale, Range);
            if (range == Range) return;
            Range = range;
            Near = unit.Near * scale;
            Splits = Detail.CascadeSplits(Near, Range);
            asset.shadowDistance = Range;
            asset.shadowCascadeCount = Splits.Count;
            if (Splits.Count == 2) asset.cascade2Split = Splits.Split1;
            else if (Splits.Count == 3) asset.cascade3Split = new Vector2(Splits.Split1, Splits.Split2);
            else asset.cascade4Split = new Vector3(Splits.Split1, Splits.Split2, Splits.Split3);
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
