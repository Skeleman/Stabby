using System.Globalization;
using System.Threading.Tasks;
using Stabby.Viewer;
using Stabby.World;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Stabby.Game
{
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateBefore(typeof(ShadowViewSystem))]
    public partial class DayNightSystem : SystemBase
    {
        public const float StepSeconds = 0.1f;
        public const string HourVariable = "STABBY_HOUR", PhaseVariable = "STABBY_MOON";
        const double FallbackTickRate = 60;
        const float ScrubHoursPerSecond = 2f;

        public static double Clock;
        public static float? PinnedHour, PinnedPhase;
        public static Quaternion? LightOverride;
        public static float ShownHour = float.NaN;
        public static bool ShownPinned;
        public static SkyState Shown;

        static readonly int SkyId = Shader.PropertyToID("_StabbySky"), GroundId = Shader.PropertyToID("_StabbyGround");
        static readonly int ZenithId = Shader.PropertyToID("_StabbyZenith"), HorizonId = Shader.PropertyToID("_StabbyHorizon"), GlowId = Shader.PropertyToID("_StabbyGlow");
        static readonly int SunDirId = Shader.PropertyToID("_StabbySunDir"), SunTintId = Shader.PropertyToID("_StabbySunTint");
        static readonly int MoonDirId = Shader.PropertyToID("_StabbyMoonDir"), MoonTintId = Shader.PropertyToID("_StabbyMoonTint"), StarsId = Shader.PropertyToID("_StabbyStars");

        Task<SkyDef> load;
        SkyDef sky;
        Light light;
        Material skyMaterial;
        float stepAt = float.MinValue;

        protected override void OnCreate()
        {
            if (World.Name != GameSettings.ViewClient) { Enabled = false; return; }
            LightOverride = null;
            ShownHour = float.NaN;
            ShownPinned = false;
            string pin = System.Environment.GetEnvironmentVariable(HourVariable);
            PinnedHour = float.TryParse(pin, NumberStyles.Float, CultureInfo.InvariantCulture, out float h) ? h : (float?)null;
            string moon = System.Environment.GetEnvironmentVariable(PhaseVariable);
            PinnedPhase = float.TryParse(moon, NumberStyles.Float, CultureInfo.InvariantCulture, out float p) ? p : (float?)null;
            load = Task.Run(ContentFiles.Sky);
        }

        protected override void OnUpdate()
        {
            Clock = SharedClock();
            if (sky == null)
            {
                if (load == null || !load.IsCompleted) return;
                if (load.IsFaulted) { GameStatus.Failure = load.Exception.ToString(); load = null; Enabled = false; return; }
                sky = load.Result;
                load = null;
                var shader = Shader.Find("Stabby/Sky");
                if (shader != null) { skyMaterial = new Material(shader) { hideFlags = HideFlags.DontSave }; RenderSettings.skybox = skyMaterial; }
            }
            Scrub();
            if (light == null)
            {
                light = RenderSettings.sun != null ? RenderSettings.sun : System.Array.Find(Object.FindObjectsByType<Light>(), l => l.type == LightType.Directional);
                if (light == null) return;
            }
            float now = UnityEngine.Time.time;
            if (now - stepAt < StepSeconds) return;
            stepAt = math.floor(now / StepSeconds) * StepSeconds;
            float hour = PinnedHour.HasValue ? Wrap(PinnedHour.Value) : sky.Hour(Clock);
            float phase = PinnedPhase.HasValue ? PinnedPhase.Value - math.floor(PinnedPhase.Value) : sky.Phase(Clock);
            Apply(sky.At(hour, phase));
            ShownPinned = PinnedHour.HasValue && PinnedPhase.HasValue;
        }

        double SharedClock()
        {
            if (!SystemAPI.TryGetSingleton(out NetworkTime time) || !time.ServerTick.IsValid) return UnityEngine.Time.timeAsDouble;
            double rate = SystemAPI.TryGetSingleton(out ClientServerTickRate tr) && tr.SimulationTickRate > 0 ? tr.SimulationTickRate : FallbackTickRate;
            return (time.ServerTick.TickIndexForValidTick + (double)time.ServerTickFraction) / rate;
        }

        void Apply(SkyState s)
        {
            var rotation = LightOverride ?? Quaternion.LookRotation(new Vector3(s.DirX, s.DirY, s.DirZ));
            if (light.transform.rotation != rotation) light.transform.rotation = rotation;
            light.color = new Color(s.Light.R, s.Light.G, s.Light.B);
            light.intensity = s.Intensity;
            var skyColor = new Color(s.Sky.R, s.Sky.G, s.Sky.B);
            var cam = Camera.main;
            if (cam != null)
            {
                cam.backgroundColor = skyColor;
                if (skyMaterial != null && cam.clearFlags != CameraClearFlags.Skybox) cam.clearFlags = CameraClearFlags.Skybox;
            }
            if (skyMaterial != null && RenderSettings.skybox != skyMaterial) RenderSettings.skybox = skyMaterial;
            RenderSettings.fogColor = skyColor;
            Shader.SetGlobalColor(HorizonId, new Color(s.Sky.R, s.Sky.G, s.Sky.B, skyMaterial != null ? 1 : 0).linear);
            Shader.SetGlobalColor(ZenithId, new Color(s.Zenith.R, s.Zenith.G, s.Zenith.B).linear);
            Shader.SetGlobalColor(GlowId, new Color(s.Glow.R, s.Glow.G, s.Glow.B).linear);
            Shader.SetGlobalVector(SunDirId, new Vector4(s.SunX, s.SunY, s.SunZ, 0));
            Shader.SetGlobalColor(SunTintId, s.Moon ? Color.black : new Color(s.Light.R, s.Light.G, s.Light.B).linear);
            Shader.SetGlobalVector(MoonDirId, new Vector4(s.MoonX, s.MoonY, s.MoonZ, s.MoonGlow));
            Shader.SetGlobalColor(MoonTintId, new Color(sky.MoonColor.R, sky.MoonColor.G, sky.MoonColor.B).linear);
            Shader.SetGlobalFloat(StarsId, s.Stars);
            Shader.SetGlobalColor(SkyId, new Color(s.AmbientSky.R, s.AmbientSky.G, s.AmbientSky.B));
            Shader.SetGlobalColor(GroundId, new Color(s.AmbientGround.R, s.AmbientGround.G, s.AmbientGround.B));
            Shown = s;
            ShownHour = s.Hour;
            GameStatus.Lines["sky"] = $"sky hour {s.Hour:F2}{(PinnedHour.HasValue ? " pinned" : "")}, moon phase {s.Phase:F2} lit {s.Illumination:F2}{(PinnedPhase.HasValue ? " pinned" : "")}, {(s.Moon ? "moon" : "sun")} {s.Intensity:F2}, sun height {s.SunHeight:F2}";
        }

        void Scrub()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.backslashKey.wasPressedThisFrame) { PinnedHour = PinnedHour.HasValue ? (float?)null : sky.Hour(Clock); stepAt = float.MinValue; }
            float dir = (kb.rightBracketKey.isPressed ? 1 : 0) - (kb.leftBracketKey.isPressed ? 1 : 0);
            if (dir == 0) return;
            PinnedHour = Wrap((PinnedHour ?? sky.Hour(Clock)) + dir * ScrubHoursPerSecond * UnityEngine.Time.unscaledDeltaTime);
        }

        static float Wrap(float h) => h - math.floor(h / 24f) * 24f;
    }
}
