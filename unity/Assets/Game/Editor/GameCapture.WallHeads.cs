using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float WallHeadHour = 12f, WallHeadOut = 5.5f, WallHeadSide = 4.5f, WallHeadUp = 1.7f;

        static System.Collections.IEnumerator WallHeadsTour(Camera cam, string dir)
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
            if (field == null) { GameStatus.Lines["~wallheads"] = GameStatus.Failure = "wallheads: no object field"; yield break; }
            int headDef = field.Library.Objects.FindIndex(d => d.Id == "wall-head");
            ObjectInstance pick = default, wall = default;
            float best = float.MaxValue;
            int heads = 0;
            for (int cz = 0; cz < field.ChunksAcross; cz++)
                for (int cx = 0; cx < field.ChunksAcross; cx++)
                {
                    var chunk = field.Chunk(cx, cz);
                    foreach (var h in chunk)
                    {
                        if (h.Def != headDef) continue;
                        heads++;
                        int touching = 0;
                        ObjectInstance near = default;
                        foreach (var a in chunk)
                        {
                            if (field.Library.Objects[a.Def].Parts[0].Shape != ObjectPart.Wall) continue;
                            float ux = 1 - 2 * a.Qy * a.Qy, uz = -2 * a.Qw * a.Qy, reach = 0.5f * Structures.WallSpan * a.Size;
                            for (int s = -1; s <= 1; s += 2)
                                if (new Vector2(a.X + s * reach * ux - h.X, a.Z + s * reach * uz - h.Z).magnitude < 0.2f * a.Size) { touching++; near = a; }
                        }
                        if (touching != 1) continue;
                        float score = new Vector2(h.X - home.x, h.Z - home.z).magnitude;
                        if (score < best) { best = score; pick = h; wall = near; }
                    }
                }
            if (best == float.MaxValue) { GameStatus.Lines["~wallheads"] = GameStatus.Failure = $"wallheads: no wall run end among {heads} heads"; yield break; }
            cam.fieldOfView = 60f;
            for (var w = ShowHour(WallHeadHour); w.MoveNext();) yield return null;
            var outward = new Vector3(pick.X - wall.X, 0, pick.Z - wall.Z).normalized;
            var side = new Vector3(outward.z, 0, -outward.x);
            var target = new Vector3(pick.X, pick.Y + 0.15f * pick.Size, pick.Z);
            var eye = target + outward * WallHeadOut + side * WallHeadSide;
            eye.y = ground.HeightAt(eye.x, eye.z) + WallHeadUp;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
            for (var w = Settle(180); w.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            Shot(cam, dir, "wallheads-after");
            var sky = DayNightSystem.Shown;
            GameStatus.Lines["~wallheads"] = $"wallheads: {heads} heads, run end at {pick.X:F0},{pick.Y:F1},{pick.Z:F0} size {pick.Size:F1} m, eye {(eye - target).magnitude:F1} m, hour {sky.Hour:F2}, GV {WorldConfig.GeneratorVersion}";
        }
    }
}
