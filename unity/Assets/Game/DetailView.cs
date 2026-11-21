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

        sealed class Key { }
        static readonly SharedStatic<DetailView> Shared = SharedStatic<DetailView>.GetOrCreate<DetailView, Key>();

        public static DetailView Current => Shared.Data;

        public static DetailView Refresh(UnityEngine.Camera cam)
        {
            int frame = Time.frameCount;
            if (Shared.Data.Frame != frame || Shared.Data.PixelsPerMetre <= 0)
            {
                Shared.Data = new DetailView
                {
                    Camera = cam.transform.position,
                    PixelsPerMetre = Detail.PixelsPerMetreAt(cam.fieldOfView),
                    Quality = math.max(GameSettings.GraphicsQuality, 0.1f),
                    Impostor = ImpostorScale,
                    Frame = frame,
                };
            }
            return Shared.Data;
        }

        public float ScreenSize(float sizeM, float3 at) => Detail.ScreenSize(sizeM, math.distance(at, Camera), PixelsPerMetre);
    }
}
