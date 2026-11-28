using System.Collections.Generic;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        static readonly int PrecipOffId = Shader.PropertyToID("_StabbyPrecipOff");

        static System.Collections.IEnumerator RainTour(Camera cam, string dir)
        {
            GameStatus.HoldCamera = true;
            cam.fieldOfView = 60f;
            var lines = new List<string>();
            PrecipSystem.PinnedPrecip = 1f;
            var low = new Vector3(home.x, GroundHeight(home.x, home.z) + 1.8f, home.z);
            for (var w = PrecipView(cam, dir, lines, low, "rain-midday"); w.MoveNext();) yield return null;
            var peak = PeakEye() + Vector3.down * 58f;
            for (var w = PrecipView(cam, dir, lines, peak, "snow-peak"); w.MoveNext();) yield return null;
            PrecipSystem.PinnedPrecip = null;
            PrecipSystem.PinnedSnow = null;
            GameStatus.Lines["~rain"] = string.Join("\n", lines);
        }

        static System.Collections.IEnumerator PrecipView(Camera cam, string dir, List<string> lines, Vector3 eye, string name)
        {
            for (var w = ShowHour(PinnedMidday); w.MoveNext();) yield return null;
            var s = DayNightSystem.Shown;
            var away = Vector3.ProjectOnPlane(-new Vector3(s.SunX, s.SunY, s.SunZ), Vector3.up).normalized;
            Vector3 look = away;
            float lowest = float.MaxValue;
            for (int k = 0; k < 16; k++)
            {
                var d = Quaternion.Euler(0, k * 22.5f, 0) * Vector3.forward;
                if (Vector3.Dot(d, away) < 0.2f) continue;
                float h = GroundHeight(eye.x + d.x * 40, eye.z + d.z * 40);
                if (h < lowest) { lowest = h; look = d; }
            }
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look + Vector3.down * 0.08f));
            for (var w = Settle(60); w.MoveNext();) yield return null;
            float natural = PrecipSystem.Snow;
            bool snowShot = name.StartsWith("snow");
            if (snowShot && natural < 1f) PrecipSystem.PinnedSnow = 1f;
            if (!snowShot && natural > 0f) PrecipSystem.PinnedSnow = 0f;
            for (var w = Settle(180); w.MoveNext();) yield return null;
            lines.Add($"{name}: eye {eye.x:F0},{eye.y:F0},{eye.z:F0}, {PrecipSystem.TemperatureC:F1} C, natural snow {natural:F2}, shown snow {PrecipSystem.Snow:F2}, weather cover {PrecipSystem.Cover:F2}, drawn {PrecipSystem.Drawn}");
            Shot(cam, null, "warmup");
            Shot(cam, dir, name);
            lines.Add(BenchPrecip(cam, name));
            PrecipSystem.PinnedSnow = null;
        }

        static string BenchPrecip(Camera cam, string label)
        {
            var ms = new double[2];
            for (int k = 0; k < 6; k++)
            {
                Shader.SetGlobalFloat(PrecipOffId, (k & 1) == 0 ? 1 : 0);
                ms[k & 1] += BenchAt(cam, 1920, 1080) / 3;
            }
            Shader.SetGlobalFloat(PrecipOffId, 0);
            return $"bench {label} precip 1080p (ms, off/on): {ms[0]:F2}/{ms[1]:F2}, cost {ms[1] - ms[0]:F2}";
        }
    }
}
