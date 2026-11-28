using System.Linq;
using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float LodestoneHour = 10f, LodestoneMoon = 0.5f, LodestoneEyeUp = 1.7f, LodestoneNearM = 18f, LodestoneAimUp = 2f, LodestoneSide = 0.35f;

        static System.Collections.IEnumerator LodestoneTour(Camera cam, string dir)
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
            if (field == null) { GameStatus.Lines["~lodestone"] = GameStatus.Failure = "lodestone: no object field"; yield break; }
            int def = field.Library.Objects.FindIndex(o => o.Id == "lodestone");
            var job = System.Threading.Tasks.Task.Run(() => field.Discoveries());
            while (!job.IsCompleted) yield return null;
            if (job.IsFaulted) { GameStatus.Lines["~lodestone"] = GameStatus.Failure = "lodestone: " + job.Exception; yield break; }
            var all = job.Result.Where(o => o.Def == def).ToArray();
            if (all.Length == 0) { GameStatus.Lines["~lodestone"] = GameStatus.Failure = "lodestone: none placed in this world"; yield break; }
            var s = all.OrderBy(o => new Vector2(o.X - home.x, o.Z - home.z).sqrMagnitude).First();
            float baseY = ground.HeightAt(s.X, s.Z);
            var centre = new Vector3(s.X, baseY, s.Z);
            cam.fieldOfView = 60f;
            for (var w = ShowHour(LodestoneHour, LodestoneMoon); w.MoveNext();) yield return null;
            var sky = DayNightSystem.Shown;
            var sun = new Vector3(sky.SunX, 0, sky.SunZ);
            if (sun.sqrMagnitude < 1e-6f) sun = Vector3.forward;
            sun.Normalize();
            var side = new Vector3(sun.z, 0, -sun.x);
            var eye = centre + (sun + side * LodestoneSide).normalized * LodestoneNearM;
            eye.y = Mathf.Max(ground.HeightAt(eye.x, eye.z) + LodestoneEyeUp, baseY + 2.5f);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(centre + Vector3.up * LodestoneAimUp - eye));
            for (var w = Settle(180); w.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            sky = DayNightSystem.Shown;
            GameStatus.Lines["~lodestone"] = $"lodestone: {all.Length} placed, shown {s.X:F0},{s.Z:F0} size {s.Size:F1} variant {s.Variant}, ground {baseY:F2} fit {field.LodestoneFit(s.X, s.Z):F2}, GV {WorldConfig.GeneratorVersion}, pinned hour {sky.Hour:F2}";
            Shot(cam, dir, "lodestone");
        }
    }
}
