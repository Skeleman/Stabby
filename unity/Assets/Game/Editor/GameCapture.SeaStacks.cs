using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        static System.Collections.IEnumerator SeaStacksTour(Camera cam, string dir)
        {
            GameStatus.HoldCamera = true;
            Ground ground = default;
            bool found = false;
            foreach (var w in Unity.Entities.World.All)
            {
                if (w.Name != GameSettings.ViewClient) continue;
                var q = w.EntityManager.CreateEntityQuery(typeof(Ground));
                if (q.CalculateEntityCount() != 1) continue;
                ground = q.GetSingleton<Ground>();
                found = true;
                break;
            }
            if (!found) { GameStatus.Lines["~seastacks"] = GameStatus.Failure = "seastacks: no ground"; yield break; }
            int at = PickStack(ground, out var s, out int count);
            if (at < 0) { GameStatus.Lines["~seastacks"] = GameStatus.Failure = "seastacks: none placed"; yield break; }
            var mid = new Vector3(0.5f * (s.Ax + s.Bx), 0.4f * s.By, 0.5f * (s.Az + s.Bz));
            Vector3 cliff = default;
            float best = float.MinValue;
            for (int k = 0; k < 32; k++)
                for (float away = 80f; away <= 420f; away += 20f)
                {
                    float a = k * Mathf.PI / 16f;
                    var e = new Vector3(mid.x + away * Mathf.Cos(a), 0f, mid.z + away * Mathf.Sin(a));
                    float h = ground.HeightAt(e.x, e.z);
                    if (h < 8f) continue;
                    e.y = h + 1.7f;
                    if (Blocked(ground, e, mid)) continue;
                    float score = Mathf.Min(h, 60f) - 0.05f * away;
                    if (score > best) { best = score; cliff = e; }
                }
            float ax = s.Bz - s.Az, az = s.Ax - s.Bx, al = Mathf.Sqrt(ax * ax + az * az);
            Vector3 sea = default;
            float bestSea = float.MinValue;
            for (int k = 0; k < 32; k++)
            {
                float a = k * Mathf.PI / 16f, away = Mathf.Max(60f, 4f * s.R);
                var e = new Vector3(mid.x + away * Mathf.Cos(a), 1.6f, mid.z + away * Mathf.Sin(a));
                if (ground.Surface(e.x, e.z) > -1f || Blocked(ground, e, new Vector3(mid.x, 2f, mid.z))) continue;
                float score = al > 0f ? Mathf.Abs(Mathf.Cos(a) * ax + Mathf.Sin(a) * az) / al : 0f;
                if (score > bestSea) { bestSea = score; sea = e; }
            }
            cam.fieldOfView = 55f;
            for (var w = ShowHour(BoulderHour, BoulderMoon); w.MoveNext();) yield return null;
            var sky = DayNightSystem.Shown;
            string info = $"seastacks: {count} stacks, edit {at} D {s.D:F1} base {s.Ay:F0} top {s.By:F0} hole {s.P:F1} at {mid.x:F0},{mid.z:F0}, GV {WorldConfig.GeneratorVersion}, pinned hour {sky.Hour:F2} moon {sky.Phase:F2}";
            if (best > float.MinValue)
            {
                cam.transform.SetPositionAndRotation(cliff, Quaternion.LookRotation(mid - cliff));
                for (var w = Settle(240); w.MoveNext();) yield return null;
                Shot(cam, null, "warmup");
                Shot(cam, dir, "seastacks-cliff");
                info += $", cliff eye {cliff.x:F0},{cliff.y:F0},{cliff.z:F0}";
            }
            else info += ", no clear cliff top";
            if (bestSea > float.MinValue)
            {
                var look = new Vector3(mid.x, Mathf.Max(3f, 0.3f * s.By), mid.z);
                cam.transform.SetPositionAndRotation(sea, Quaternion.LookRotation(look - sea));
                for (var w = Settle(240); w.MoveNext();) yield return null;
                Shot(cam, null, "warmup");
                Shot(cam, dir, "seastacks-sea");
                info += $", sea eye {sea.x:F0},{sea.z:F0}";
            }
            else info += ", no open sea eye";
            GameStatus.Lines["~seastacks"] = info;
        }

        static bool Blocked(Ground ground, Vector3 from, Vector3 to)
        {
            for (int k = 1; k < 24; k++)
            {
                var p = Vector3.Lerp(from, to, k / 24f);
                if (p.y < ground.HeightAt(p.x, p.z) + 0.5f) return true;
            }
            return false;
        }

        static int PickStack(Ground ground, out FeatureEdit s, out int count)
        {
            ref var g = ref ground.Blob.Value;
            int at = -1;
            count = 0;
            for (int k = 0; k < g.Edits.Length; k++)
            {
                var e = g.Edits[k];
                if (e.Shape != FeatureEdit.SeaStack) continue;
                count++;
                if (at < 0 || (e.P > 0f) != (g.Edits[at].P > 0f) ? at < 0 || e.P > 0f : e.By > g.Edits[at].By) at = k;
            }
            s = at < 0 ? default : g.Edits[at];
            return at;
        }
    }
}
