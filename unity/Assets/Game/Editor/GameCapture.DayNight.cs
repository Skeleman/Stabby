using System.Collections.Generic;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        public const float PinnedMidday = 12f, NightHour = 0f, DuskHour = 18.15f;

        public static void PinTime()
        {
            if (System.Environment.GetEnvironmentVariable(DayNightSystem.HourVariable) == null)
                System.Environment.SetEnvironmentVariable(DayNightSystem.HourVariable, PinnedMidday.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (System.Environment.GetEnvironmentVariable(DayNightSystem.PhaseVariable) == null)
                System.Environment.SetEnvironmentVariable(DayNightSystem.PhaseVariable, Stabby.World.SkyDef.FullMoon.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        public static void RequirePinnedTime(string what)
        {
            if (DayNightSystem.ShownPinned || GameStatus.Failure != null) return;
            GameStatus.Failure = $"{what} ran without a pinned time of day and moon phase; set {DayNightSystem.HourVariable} and {DayNightSystem.PhaseVariable}, or DayNightSystem.PinnedHour and PinnedPhase";
        }

        static System.Collections.IEnumerator ShowHour(float hour, float phase = Stabby.World.SkyDef.FullMoon)
        {
            DayNightSystem.PinnedHour = hour;
            DayNightSystem.PinnedPhase = phase;
            for (int k = 0; k < 600 && !(DayNightSystem.ShownPinned && Mathf.Abs(DayNightSystem.ShownHour - hour) < 1e-3f && Mathf.Abs(DayNightSystem.Shown.Phase - phase) < 1e-3f); k++) yield return null;
        }

        static System.Collections.IEnumerator NightTour(Camera cam, string dir)
        {
            GameStatus.HoldCamera = true;
            cam.fieldOfView = 60f;
            var lines = new List<string>();
            float homeY = GroundHeight(home.x, home.z);
            Vector3 low = home, high = home;
            float lowest = float.MaxValue;
            for (int a = 0; a < 32; a++)
            {
                var d = Quaternion.Euler(0, a * 11.25f, 0) * Vector3.forward;
                for (float r = 400f; r <= 2400f; r += 100f)
                {
                    float h = GroundHeight(home.x + d.x * r, home.z + d.z * r);
                    if (h < lowest) { lowest = h; low = new Vector3(home.x + d.x * r, h, home.z + d.z * r); }
                }
            }
            var toLow = Vector3.ProjectOnPlane(low - home, Vector3.up).normalized;
            float tallest = float.MinValue;
            for (float r = -1200f; r <= 0f; r += 50f)
            {
                float h = GroundHeight(home.x + toLow.x * r, home.z + toLow.z * r);
                if (h > tallest) { tallest = h; high = new Vector3(home.x + toLow.x * r, h, home.z + toLow.z * r); }
            }
            var eye = high + Vector3.up * 60f;
            var focus = Vector3.Lerp(high, low, 0.75f);
            focus.y = Mathf.Max(GroundHeight(focus.x, focus.z), low.y);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
            for (var w = ShowHour(NightHour); w.MoveNext();) yield return null;
            for (var w = Settle(360); w.MoveNext();) yield return null;
            var s = DayNightSystem.Shown;
            lines.Add($"night hour {s.Hour:F2}, full moon: {(s.Moon ? "moon" : "sun")} light {s.Intensity:F3}, lit {s.Illumination:F2}, ambient sky {s.AmbientSky.B:F3}, sun height {s.SunHeight:F2}, moon height {s.MoonHeight:F2}");
            lines.Add($"eye {eye.x:F0},{eye.y:F0},{eye.z:F0} over valley floor {low.y:F0} m (home ground {homeY:F0} m), {Vector3.Distance(eye, focus):F0} m to focus");
            Shot(cam, null, "warmup");
            Shot(cam, dir, "night");
            for (var w = ShowHour(NightHour, 0f); w.MoveNext();) yield return null;
            for (var w = Settle(30); w.MoveNext();) yield return null;
            s = DayNightSystem.Shown;
            lines.Add($"night hour {s.Hour:F2}, new moon: {(s.Moon ? "moon" : "sun")} light {s.Intensity:F4}, lit {s.Illumination:F2}, ambient sky {s.AmbientSky.B:F4}, sky {s.Sky.R:F3},{s.Sky.G:F3},{s.Sky.B:F3}");
            Shot(cam, null, "warmup");
            Shot(cam, dir, "night-newmoon");
            for (var w = ShowHour(DuskHour); w.MoveNext();) yield return null;
            var sun = DayNightSystem.Shown;
            var west = Vector3.ProjectOnPlane(-new Vector3(sun.DirX, sun.DirY, sun.DirZ), Vector3.up).normalized;
            var duskEye = home;
            float peak = float.MinValue;
            for (int a = 0; a < 32; a++)
            {
                var d = Quaternion.Euler(0, a * 11.25f, 0) * Vector3.forward;
                for (float r = 0f; r <= 1500f; r += 100f)
                {
                    float h = GroundHeight(home.x + d.x * r, home.z + d.z * r);
                    if (h > peak) { peak = h; duskEye = new Vector3(home.x + d.x * r, h + 60f, home.z + d.z * r); }
                }
            }
            cam.transform.SetPositionAndRotation(duskEye, Quaternion.LookRotation(Quaternion.AngleAxis(-25f, Vector3.up) * west + Vector3.down * 0.12f));
            for (var w = Settle(360); w.MoveNext();) yield return null;
            sun = DayNightSystem.Shown;
            lines.Add($"dusk hour {sun.Hour:F2}: {(sun.Moon ? "moon" : "sun")} light {sun.Intensity:F3}, sun height {sun.SunHeight:F3}, eye {duskEye.x:F0},{duskEye.y:F0},{duskEye.z:F0}");
            Shot(cam, null, "warmup");
            Shot(cam, dir, "dusk");
            for (var w = ShowHour(PinnedMidday); w.MoveNext();) yield return null;
            GameStatus.Lines["~daynight"] = string.Join("\n", lines);
        }
    }
}
