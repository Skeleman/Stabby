using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        static System.Collections.IEnumerator ButteTour(Camera cam, string dir)
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
            if (!found) { GameStatus.Lines["~butte"] = GameStatus.Failure = "butte: no ground"; yield break; }
            int at = TallestButte(ground, out var b);
            if (at < 0) { GameStatus.Lines["~butte"] = GameStatus.Failure = "butte: none placed"; yield break; }
            float rise = b.By - b.Ay, away = 7f * b.R;
            var mid = new Vector3(b.Ax, b.Ay + 0.45f * rise, b.Az);
            Vector3 eye = default;
            float clear = float.MinValue;
            for (int k = 0; k < 16; k++)
            {
                float a = k * Mathf.PI / 8f;
                var e = new Vector3(b.Ax + away * Mathf.Cos(a), 0f, b.Az + away * Mathf.Sin(a));
                e.y = Mathf.Max(ground.HeightAt(e.x, e.z) + 1.7f, b.Ay + 0.25f * rise);
                float worst = float.MaxValue;
                for (int s = 1; s < 20; s++)
                {
                    var p = Vector3.Lerp(e, mid, s / 20f);
                    worst = Mathf.Min(worst, p.y - ground.HeightAt(p.x, p.z));
                }
                worst -= 0.02f * (e.y - b.Ay);
                if (worst > clear) { clear = worst; eye = e; }
            }
            cam.fieldOfView = 50f;
            for (var w = ShowHour(BoulderHour, BoulderMoon); w.MoveNext();) yield return null;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(mid - eye));
            for (var w = Settle(240); w.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            var sky = DayNightSystem.Shown;
            GameStatus.Lines["~butte"] = $"butte: edit {at} cap {b.D:F0} rise {rise:F0} R {b.R:F0} at {b.Ax:F0},{b.Ay:F0},{b.Az:F0} eye {eye.x:F0},{eye.y:F0},{eye.z:F0} clear {clear:F1}, GV {WorldConfig.GeneratorVersion}, pinned hour {sky.Hour:F2} moon {sky.Phase:F2}";
            Shot(cam, dir, "butte-far");
        }

        static int TallestButte(Ground ground, out FeatureEdit b)
        {
            ref var g = ref ground.Blob.Value;
            int at = -1;
            for (int k = 0; k < g.Edits.Length; k++)
                if (g.Edits[k].Shape == FeatureEdit.Butte && (at < 0 || g.Edits[k].By - g.Edits[k].Ay > g.Edits[at].By - g.Edits[at].Ay)) at = k;
            b = at < 0 ? default : g.Edits[at];
            return at;
        }
    }
}
