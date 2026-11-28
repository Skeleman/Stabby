using Stabby.World;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Stabby.Game
{
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(DayNightSystem))]
    public partial class PrecipSystem : SystemBase
    {
        public const float GroundWind = 0.25f;
        static readonly int WeatherId = Shader.PropertyToID("_StabbyWeather"), RainId = Shader.PropertyToID("_StabbyRain"), SnowId = Shader.PropertyToID("_StabbySnow");
        static readonly int RainLookId = Shader.PropertyToID("_StabbyRainLook"), SnowLookId = Shader.PropertyToID("_StabbySnowLook"), RainShiftId = Shader.PropertyToID("_StabbyRainShift");
        static readonly int SnowShiftId = Shader.PropertyToID("_StabbySnowShift"), BoxId = Shader.PropertyToID("_StabbyPrecipBox");
        static readonly int RainColorId = Shader.PropertyToID("_StabbyRainColor"), SnowColorId = Shader.PropertyToID("_StabbySnowColor");
        public static float? PinnedPrecip, PinnedSnow;
        public static float Cover, Precip, Snow, TemperatureC;
        public static int Drawn;
        Material material;
        WorldConfig config;

        protected override void OnCreate()
        {
            if (World.Name != GameSettings.ViewClient) { Enabled = false; return; }
            config = MapGen.Config(GameSettings.ActiveSeed);
        }

        protected override void OnDestroy()
        {
            if (material != null) Object.Destroy(material);
            Shader.SetGlobalVector(WeatherId, Vector4.zero);
        }

        protected override void OnUpdate()
        {
            Drawn = 0;
            var sky = DayNightSystem.Loaded;
            var cam = Camera.main;
            if (sky == null || cam == null || !SystemAPI.TryGetSingleton(out Ground ground)) return;
            var w = sky.Weather;
            double clock = DayNightSystem.Clock;
            w.At(ground.Blob.Value.Seed, clock, out Cover, out Precip);
            if (PinnedPrecip.HasValue) Precip = PinnedPrecip.Value;
            Vector3 eye = cam.transform.position;
            TemperatureC = ClimateField.TemperatureAt(config, eye.y);
            Snow = PinnedSnow ?? w.SnowFraction(TemperatureC);
            Shader.SetGlobalVector(WeatherId, new Vector4(Cover, Precip, Snow, TemperatureC));
            int count = (int)(math.lerp(w.Rain.Count, w.Snow.Count, Snow) * Precip);
            if (count <= 0) return;
            if (material == null)
            {
                var shader = Shader.Find("Stabby/Precip");
                if (shader == null) { GameStatus.Failure = "Stabby/Precip shader missing"; Enabled = false; return; }
                material = new Material(shader) { name = "Stabby precipitation" };
            }
            float ppm = Detail.PixelsPerMetreAt(cam.fieldOfView);
            float rainBox = 2 * Detail.Reach(math.sqrt(w.Rain.LengthM * w.Rain.WidthM), w.MinPx, ppm);
            float snowBox = 2 * Detail.Reach(math.sqrt(w.Snow.LengthM * w.Snow.WidthM), w.MinPx, ppm);
            float a = sky.Clouds.WindDirDeg * Mathf.Deg2Rad, wind = sky.Clouds.WindSpeedMs * GroundWind;
            var gust = new Vector3(math.sin(a) * wind, 0, math.cos(a) * wind);
            var rainVel = gust + Vector3.down * w.Rain.FallMs;
            var snowVel = gust * 0.5f + Vector3.down * w.Snow.FallMs;
            Shader.SetGlobalVector(RainId, new Vector4(rainVel.x, rainVel.y, rainVel.z, rainBox));
            Shader.SetGlobalVector(SnowId, new Vector4(snowVel.x, snowVel.y, snowVel.z, snowBox));
            Shader.SetGlobalVector(RainShiftId, Shift(rainVel, rainBox, clock));
            Shader.SetGlobalVector(SnowShiftId, Shift(snowVel, snowBox, clock));
            Shader.SetGlobalVector(RainLookId, Look(w.Rain));
            Shader.SetGlobalVector(SnowLookId, Look(w.Snow));
            Shader.SetGlobalVector(BoxId, new Vector4(Snow, w.Snow.SwayM, w.MinPx, Mathf.Clamp01(DayNightSystem.Shown.Intensity)));
            Shader.SetGlobalColor(RainColorId, new Color(w.Rain.Color.R, w.Rain.Color.G, w.Rain.Color.B, w.Rain.Alpha).linear);
            Shader.SetGlobalColor(SnowColorId, new Color(w.Snow.Color.R, w.Snow.Color.G, w.Snow.Color.B, w.Snow.Alpha).linear);
            var rp = new RenderParams(material)
            {
                worldBounds = new Bounds(eye, Vector3.one * math.max(rainBox, snowBox)),
                shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
                receiveShadows = false,
                motionVectorMode = MotionVectorGenerationMode.ForceNoMotion,
            };
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, 6, count);
            Drawn = count;
        }

        static Vector4 Look(PrecipLook p) => new Vector4(p.LengthM, p.WidthM, 0, 0);

        static Vector4 Shift(Vector3 vel, float box, double clock)
        {
            double x = vel.x * clock / box, y = vel.y * clock / box, z = vel.z * clock / box;
            return new Vector4((float)(x - System.Math.Floor(x)), (float)(y - System.Math.Floor(y)), (float)(z - System.Math.Floor(z)), 0);
        }
    }
}
