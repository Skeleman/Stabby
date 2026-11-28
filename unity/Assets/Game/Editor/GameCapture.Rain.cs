using System.Collections.Generic;
using Stabby.World;
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
            for (var w = PrecipView(cam, dir, lines, low, "rain-night", NightHour); w.MoveNext();) yield return null;
            if (ShelterSpot(out var inside, out var outside, out var why)) { for (var w = PrecipView(cam, dir, lines, inside, "rain-shelter", PinnedMidday, outside); w.MoveNext();) yield return null; }
            else GameStatus.Failure = why;
            lines.Add(why);
            PrecipSystem.PinnedPrecip = null;
            PrecipSystem.PinnedSnow = null;
            GameStatus.Lines["~rain"] = string.Join("\n", lines);
        }

        static bool ShelterSpot(out Vector3 inside, out Vector3 outside, out string why)
        {
            inside = outside = default;
            why = "rain-shelter: no cave mouth found";
            Ground ground = default;
            bool found = false;
            foreach (var world in Unity.Entities.World.All)
            {
                var q = world.EntityManager.CreateEntityQuery(typeof(Ground));
                if (q.CalculateEntityCount() != 1) continue;
                ground = q.GetSingleton<Ground>();
                found = true;
                break;
            }
            if (!found) return false;
            ref var edits = ref ground.Blob.Value.Edits;
            float best = float.MaxValue, s = ground.WalkStep;
            for (int k = 0; k < edits.Length; k++)
            {
                var e = edits[k];
                if (!e.CaveCarve || e.R < 2.5f) continue;
                for (int end = 0; end < 2; end++)
                {
                    var a = end == 0 ? new Vector3(e.Ax, e.Ay, e.Az) : new Vector3(e.Bx, e.By, e.Bz);
                    var b = end == 0 ? new Vector3(e.Bx, e.By, e.Bz) : new Vector3(e.Ax, e.Ay, e.Az);
                    float len = Vector3.Distance(a, b), far = Vector3.Distance(a, home);
                    if (len < 8f || far >= best) continue;
                    var at = ground.EditsNear(a.x, a.z, 0);
                    float floorA = Walk.Surface(ground, at, s, a.x, a.z, a.y + e.R + 40f, a.y - 2f * e.R);
                    if (Shelter.Covered(ground, at, s, a.x, floorA + 1.6f, a.z)) continue;
                    for (float t = 2f; t < Mathf.Min(len, 16f); t += 1f)
                    {
                        var p = Vector3.Lerp(a, b, t / len);
                        var ep = ground.EditsNear(p.x, p.z, 0);
                        float floor = Walk.Surface(ground, ep, s, p.x, p.z, p.y, p.y - 2f * e.R);
                        float y = floor + 1.6f;
                        if (floor <= p.y - 2f * e.R || Walk.Solid(ground, ep, s, p.x, y, p.z) >= 0 || !Shelter.Covered(ground, ep, s, p.x, y, p.z)) continue;
                        var back = Vector3.Lerp(a, b, Mathf.Min(len, t + 4f) / len);
                        var eb = ground.EditsNear(back.x, back.z, 0);
                        float fb = Walk.Surface(ground, eb, s, back.x, back.z, back.y, back.y - 2f * e.R);
                        if (fb <= back.y - 2f * e.R || Walk.Solid(ground, eb, s, back.x, fb + 1.6f, back.z) >= 0) break;
                        best = far;
                        inside = new Vector3(back.x, fb + 1.6f, back.z);
                        outside = new Vector3(a.x, floorA + 2f, a.z);
                        why = $"rain-shelter: cave edit {k} shape {e.Shape} R {e.R:F1}, mouth {a.x:F0},{floorA:F1},{a.z:F0}, {far:F0} m from home, eye {inside.x:F0},{inside.y:F1},{inside.z:F0}";
                        break;
                    }
                }
            }
            return best < float.MaxValue;
        }

        static System.Collections.IEnumerator PrecipView(Camera cam, string dir, List<string> lines, Vector3 eye, string name, float hour = PinnedMidday, Vector3? aim = null)
        {
            for (var w = ShowHour(hour); w.MoveNext();) yield return null;
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
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim.HasValue ? aim.Value - eye : look + Vector3.down * 0.08f));
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
