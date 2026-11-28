using System.Collections.Generic;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        static readonly int CloudsOffId = Shader.PropertyToID("_StabbyCloudsOff");

        static System.Collections.IEnumerator CloudsTour(Camera cam, string dir)
        {
            GameStatus.HoldCamera = true;
            cam.fieldOfView = 60f;
            var lines = new List<string>();
            PrecipSystem.PinnedPrecip = 0f;
            PrecipSystem.PinnedCover = 0.45f;
            var eye = PeakEye();
            for (var w = SkyView(cam, dir, lines, eye, PinnedMidday, Stabby.World.SkyDef.FullMoon, "clouds-noon", false, 90f, -0.05f); w.MoveNext();) yield return null;
            lines.Add(BenchClouds(cam, "noon"));
            for (var w = SkyView(cam, dir, lines, eye, DuskHour, Stabby.World.SkyDef.FullMoon, "clouds-dusk", false, -25f, -0.12f); w.MoveNext();) yield return null;
            lines.Add(BenchClouds(cam, "dusk"));
            cam.transform.rotation = Quaternion.LookRotation(Vector3.up * 0.6f + cam.transform.forward);
            for (var w = Settle(30); w.MoveNext();) yield return null;
            lines.Add(BenchClouds(cam, "dusk sky-filled"));
            for (var w = ShowHour(PinnedMidday); w.MoveNext();) yield return null;
            PrecipSystem.PinnedPrecip = null;
            PrecipSystem.PinnedCover = null;
            GameStatus.Lines["~clouds"] = string.Join("\n", lines);
        }

        static string BenchClouds(Camera cam, string label)
        {
            var ms = new double[2];
            for (int k = 0; k < 6; k++)
            {
                Shader.SetGlobalFloat(CloudsOffId, (k & 1) == 0 ? 1 : 0);
                ms[k & 1] += BenchAt(cam, 1920, 1080) / 3;
            }
            Shader.SetGlobalFloat(CloudsOffId, 0);
            return $"bench {label} clouds 1080p (ms, off/on): {ms[0]:F2}/{ms[1]:F2}, cost {ms[1] - ms[0]:F2}";
        }
    }
}
