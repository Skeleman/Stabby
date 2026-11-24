using Stabby.World;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

namespace Stabby.Game
{
    public struct DetailView
    {
        public float3 Camera;
        public float PixelsPerMetre, Quality, Impostor;
        public static float ImpostorScale = 1f;
        public int Frame;
        public Detail.ShadowMap Shadows;

        sealed class Key { }
        static readonly SharedStatic<DetailView> Shared = SharedStatic<DetailView>.GetOrCreate<DetailView, Key>();

        public static DetailView Current => Shared.Data;

        public static DetailView Refresh(UnityEngine.Camera cam)
        {
            int frame = Time.frameCount;
            if (Shared.Data.Frame != frame || Shared.Data.PixelsPerMetre <= 0)
            {
                GameSettings.UseLines(cam.pixelHeight);
                Shared.Data = new DetailView
                {
                    Camera = cam.transform.position,
                    PixelsPerMetre = Detail.PixelsPerMetreAt(cam.fieldOfView),
                    Quality = math.max(GameSettings.GraphicsQuality, 0.1f),
                    Impostor = ImpostorScale,
                    Frame = frame,
                    Shadows = ShadowViewSystem.Map,
                };
            }
            return Shared.Data;
        }

        public static void SetShadows(in Detail.ShadowMap map) => Shared.Data.Shadows = map;

        public float ScreenSize(float sizeM, float3 at) => Detail.ScreenSize(sizeM, math.distance(at, Camera), PixelsPerMetre);

        public Detail.Decision Decide(in Detail.Rule rule, float sizeM, float3 at, Detail.Decision previous) =>
            Detail.Decide(rule, sizeM, math.distance(at, Camera), PixelsPerMetre, Quality, Shadows, previous);
    }
}
