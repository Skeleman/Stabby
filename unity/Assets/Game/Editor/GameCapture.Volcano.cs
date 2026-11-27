using System.Linq;
using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float VolcanoHour = 15.5f, VolcanoMoon = 0.5f, VolcanoFlankAway = 1.5f, VolcanoFlankUp = 0.45f, VolcanoFarNear = 400f, VolcanoFarFar = 700f, VolcanoFarEye = 4f, VolcanoBackdropM = 1500f;

        static System.Collections.IEnumerator VolcanoTour(Camera cam, string dir)
        {
            GameStatus.HoldCamera = true;
            ObjectField field = null;
            Ground ground = default;
            foreach (var w in Unity.Entities.World.All)
            {
                var f = w.Name == GameSettings.ViewClient ? w.GetExistingSystemManaged<ObjectColumnsSystem>()?.Field : null;
                var q = f == null ? default : w.EntityManager.CreateEntityQuery(typeof(Ground));
                if (f == null || q.CalculateEntityCount() != 1) continue;
                field = f;
                ground = q.GetSingleton<Ground>();
                break;
            }
            if (field == null) { GameStatus.Lines["~volcano"] = GameStatus.Failure = "volcano: no object field"; yield break; }
            var job = System.Threading.Tasks.Task.Run(() => field.VolcanoPools());
            while (!job.IsCompleted) yield return null;
            if (job.IsFaulted) { GameStatus.Lines["~volcano"] = GameStatus.Failure = "volcano: " + job.Exception; yield break; }
            if (job.Result.Length == 0) { GameStatus.Lines["~volcano"] = GameStatus.Failure = "volcano: none placed in this world"; yield break; }
            var pool = job.Result.OrderBy(p => new Vector2(p.X - home.x, p.Z - home.z).sqrMagnitude).First();
            var v = field.VolcanoOf(pool);
            var crater = new Vector3(v.X, pool.Level, v.Z);
            float top = v.Base + v.H;
            cam.fieldOfView = 55f;
            for (var w = ShowHour(VolcanoHour, VolcanoMoon); w.MoveNext();) yield return null;
            var flank = ClearEye(ground, crater, VolcanoFlankAway * v.Ro, top + VolcanoFlankUp * v.H, out float flankClear);
            cam.transform.SetPositionAndRotation(flank, Quaternion.LookRotation(crater - flank));
            for (var w = Settle(240); w.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            var sky = DayNightSystem.Shown;
            Shot(cam, dir, "volcano-flank");
            var mid = new Vector3(v.X, v.Base + 0.5f * v.H, v.Z);
            var far = PlateauEye(ground, mid, v.Base, v.H, v.Ro, out float farClear);
            cam.transform.SetPositionAndRotation(far, Quaternion.LookRotation(mid - far));
            for (var w = Settle(180); w.MoveNext();) yield return null;
            Shot(cam, dir, "volcano-far");
            GameStatus.Lines["~volcano"] = $"volcano: {job.Result.Length} placed, shown {v.X:F0},{v.Z:F0} Ro {v.Ro:F0} Rc {v.Rc:F0} H {v.H:F0} base {v.Base:F0} rim {v.RimLow:F0} pool {(v.Active ? "lava" : "water")} {pool.Level:F1} breached {v.Breached}, flank eye {flank.x:F0},{flank.y:F0},{flank.z:F0} clear {flankClear:F1}, far eye {far.x:F0},{far.y:F0},{far.z:F0} clear {farClear:F1}, GV {WorldConfig.GeneratorVersion}, pinned hour {sky.Hour:F2} moon {sky.Phase:F2}";
        }

        static Vector3 PlateauEye(Ground ground, Vector3 aim, float baseY, float h, float ro, out float clear)
        {
            Vector3 eye = default;
            float best = float.MinValue;
            clear = float.MinValue;
            for (float away = VolcanoFarNear; away <= VolcanoFarFar; away += 50f)
                for (int k = 0; k < 32; k++)
                {
                    float a = k * Mathf.PI / 16f;
                    var e = new Vector3(aim.x + away * Mathf.Cos(a), 0f, aim.z + away * Mathf.Sin(a));
                    float g = ground.HeightAt(e.x, e.z);
                    if (Mathf.Abs(g - baseY) > 0.5f * h) continue;
                    e.y = g + VolcanoFarEye;
                    float sight = float.MaxValue;
                    for (int s = 1; s < 24; s++)
                    {
                        var p = Vector3.Lerp(e, aim, s / 24f);
                        sight = Mathf.Min(sight, p.y - ground.HeightAt(p.x, p.z));
                    }
                    if (sight < 0f) continue;
                    var dir = new Vector3(aim.x - e.x, 0f, aim.z - e.z) / away;
                    float topSlope = (baseY + h - e.y) / away, behind = float.MinValue;
                    for (float d = away + ro * 1.2f; d < away + VolcanoBackdropM; d += 50f)
                        behind = Mathf.Max(behind, (ground.HeightAt(e.x + dir.x * d, e.z + dir.z * d) - e.y) / d);
                    float score = topSlope - Mathf.Max(0f, behind) - 0.0002f * Mathf.Abs(away - 550f);
                    if (score > best) { best = score; clear = sight; eye = e; }
                }
            if (best == float.MinValue) return ClearEye(ground, aim, VolcanoFarNear, baseY + 0.3f * h, out clear);
            return eye;
        }

        static Vector3 ClearEye(Ground ground, Vector3 aim, float away, float lowY, out float clear)
        {
            Vector3 eye = default;
            clear = float.MinValue;
            for (int k = 0; k < 16; k++)
            {
                float a = k * Mathf.PI / 8f;
                var e = new Vector3(aim.x + away * Mathf.Cos(a), 0f, aim.z + away * Mathf.Sin(a));
                e.y = Mathf.Max(ground.HeightAt(e.x, e.z) + 1.7f, lowY);
                float worst = float.MaxValue;
                for (int s = 1; s < 20; s++)
                {
                    var p = Vector3.Lerp(e, aim, s / 20f);
                    worst = Mathf.Min(worst, p.y - ground.HeightAt(p.x, p.z));
                }
                worst -= 0.02f * (e.y - lowY);
                if (worst > clear) { clear = worst; eye = e; }
            }
            return eye;
        }
    }
}
