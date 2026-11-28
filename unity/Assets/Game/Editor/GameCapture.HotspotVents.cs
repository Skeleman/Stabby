using System.Collections.Generic;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float VentFillShare = 0.33f, VentFov = 50f, VentEyeAbove = 1.7f, MudFillShare = 0.5f, MudEyeAbove = 8f;

        static System.Collections.IEnumerator HotspotVentsTour(Camera cam, string dir)
        {
            GameStatus.HoldCamera = true;
            cam.fieldOfView = VentFov;
            ObjectField field = null;
            Ground ground = default;
            foreach (var w in Unity.Entities.World.All)
            {
                if (w.Name != GameSettings.ViewClient) continue;
                field = w.GetExistingSystemManaged<ObjectColumnsSystem>()?.Field;
                var q = w.EntityManager.CreateEntityQuery(typeof(Ground));
                if (q.CalculateEntityCount() == 1) ground = q.GetSingleton<Ground>();
            }
            if (field == null) { GameStatus.Lines["~hotspotvents"] = GameStatus.Failure = "hotspotvents: no object field"; yield break; }
            for (var w = ShowHour(PinnedMidday); w.MoveNext();) yield return null;
            int[] kinds = { ObjectField.MudPot, ObjectField.Geyser };
            string[] names = { "hotspots-mudpot", "hotspots-geyser" };
            var report = new List<string>();
            var pools = new List<float>();
            for (int k = 0; k < kinds.Length; k++)
            {
                var pick = default(ObjectInstance);
                float best = float.MaxValue;
                foreach (var o in field.Discoveries())
                {
                    if (!field.IsVent(o) || field.VentKind(o) != kinds[k]) continue;
                    float d = new Vector2(o.X - home.x, o.Z - home.z).magnitude;
                    if (d < best) { best = d; pick = o; }
                }
                if (best == float.MaxValue) { report.Add(names[k] + ": none placed"); continue; }
                field.SpringPoolsAt(pick, pools);
                int count = pools.Count / ObjectField.SpringStride;
                float cx = 0, cz = 0;
                for (int p = 0; p < pools.Count; p += ObjectField.SpringStride) { cx += pools[p]; cz += pools[p + 1]; }
                cx /= count;
                cz /= count;
                float reach = 0;
                for (int p = 0; p < pools.Count; p += ObjectField.SpringStride)
                    reach = Mathf.Max(reach, new Vector2(pools[p] - cx, pools[p + 1] - cz).magnitude + pools[p + 3]);
                var aim = new Vector3(cx, pools[4], cz);
                float share = kinds[k] == ObjectField.MudPot ? MudFillShare : VentFillShare;
                float away = reach / (share * Mathf.Tan(VentFov * 0.5f * Mathf.Deg2Rad));
                var eye = ClearEye(ground, aim, away, 0f, out float clear);
                eye.y = ground.HeightAt(eye.x, eye.z) + (kinds[k] == ObjectField.MudPot ? MudEyeAbove : VentEyeAbove);
                if (kinds[k] == ObjectField.Geyser)
                {
                    Structures.GeyserCycle(field.VentSeed(pick), out float interval, out float duration, out float offset);
                    TerrainViewSystem.ClockOverride = offset + 0.4 * duration;
                }
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim - eye));
                for (var e = Settle(300); e.MoveNext();) yield return null;
                Shot(cam, null, "warmup");
                Shot(cam, dir, names[k]);
                TerrainViewSystem.ClockOverride = null;
                report.Add($"{names[k]}: at {pick.X:F0},{pick.Z:F0} reach {reach:F1} m, eye {away:F0} m out at {eye.x:F0},{eye.y:F0},{eye.z:F0}, clear {clear:F1}");
            }
            GameStatus.Lines["~hotspotvents"] = string.Join("\n", report);
        }
    }
}
