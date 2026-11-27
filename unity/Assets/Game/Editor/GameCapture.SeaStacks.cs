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
            var group = Cluster(ground, s, out var centre, out float top);
            var aim = new Vector3(centre.x, 0.3f * top, centre.z);
            Vector3 cliff = default;
            float best = float.MinValue;
            for (int pass = 0; pass < 2 && best == float.MinValue; pass++)
                for (int k = 0; k < 64; k++)
                    for (float away = pass == 0 ? 150f : 100f; away <= (pass == 0 ? 250f : 420f); away += 10f)
                    {
                        float a = k * Mathf.PI / 32f;
                        var e = new Vector3(centre.x + away * Mathf.Cos(a), 0f, centre.z + away * Mathf.Sin(a));
                        float h = ground.HeightAt(e.x, e.z);
                        if (h < 8f) continue;
                        e.y = h + 1.7f;
                        if (Blocked(ground, e, aim) || !Frames(ground, e, aim, group)) continue;
                        float score = Mathf.Min(h, 60f) - 0.1f * Mathf.Abs(away - 200f);
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
                cam.transform.SetPositionAndRotation(cliff, Quaternion.LookRotation(aim - cliff));
                for (var w = Settle(240); w.MoveNext();) yield return null;
                Shot(cam, null, "warmup");
                Shot(cam, dir, "seastacks-cliff");
                info += $", cliff eye {cliff.x:F0},{cliff.y:F0},{cliff.z:F0} {Vector2.Distance(new Vector2(cliff.x, cliff.z), new Vector2(centre.x, centre.z)):F0} m from {group.Count} stacks";
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

        static System.Collections.Generic.List<Vector3> Cluster(Ground ground, FeatureEdit s, out Vector3 centre, out float top)
        {
            ref var g = ref ground.Blob.Value;
            var list = new System.Collections.Generic.List<Vector3>();
            float mx = 0.5f * (s.Ax + s.Bx), mz = 0.5f * (s.Az + s.Bz);
            centre = Vector3.zero;
            top = 0f;
            for (int k = 0; k < g.Edits.Length; k++)
            {
                var e = g.Edits[k];
                if (e.Shape != FeatureEdit.SeaStack) continue;
                float x = 0.5f * (e.Ax + e.Bx), z = 0.5f * (e.Az + e.Bz);
                if ((x - mx) * (x - mx) + (z - mz) * (z - mz) > 300f * 300f) continue;
                list.Add(new Vector3(x, e.By, z));
                centre += new Vector3(x, 0f, z);
                top = Mathf.Max(top, e.By);
            }
            centre /= list.Count;
            return list;
        }

        static bool Frames(Ground ground, Vector3 eye, Vector3 aim, System.Collections.Generic.List<Vector3> group)
        {
            var f = aim - eye;
            f.y = 0f;
            f.Normalize();
            foreach (var p in group)
            {
                var d = p - eye;
                d.y = 0f;
                if (Vector3.Angle(f, d) > 32f || Blocked(ground, eye, p)) return false;
            }
            return true;
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
