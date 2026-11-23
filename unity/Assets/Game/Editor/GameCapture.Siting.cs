using System.Collections.Generic;
using UnityEngine;
using Stabby.World;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float ClusterReachM = 120f;

        static System.Collections.IEnumerator SitingTour(Camera cam, string dir)
        {
            cam.fieldOfView = 60f;
            if (AimDenCliff(cam))
            {
                for (var e = Settle(240); e.MoveNext();) yield return null;
                Shot(cam, null, "warmup");
                Shot(cam, dir, "den-cliff");
            }
            else GameStatus.Lines["~den-cliff"] = "no wolf den placed";
            if (Aim(cam, FeatureEdit.Arch, false, false))
            {
                for (var e = Settle(240); e.MoveNext();) yield return null;
                Shot(cam, null, "warmup");
                Shot(cam, dir, "arch-channel");
            }
            else GameStatus.Lines["~arch-channel"] = "no arch placed";
            if (AimVentCluster(cam))
            {
                for (var e = Settle(360); e.MoveNext();) yield return null;
                Shot(cam, null, "warmup");
                Shot(cam, dir, "vent-cluster");
                TerrainViewSystem.ClockOverride = null;
            }
            else GameStatus.Lines["~vent-cluster"] = "no hotspot holds two vents";
        }

        static bool AimDenCliff(Camera cam)
        {
            foreach (var w in Unity.Entities.World.All)
            {
                var field = w.Name == GameSettings.ViewClient ? w.GetExistingSystemManaged<ObjectColumnsSystem>()?.Field : null;
                if (field == null) continue;
                ObjectInstance pick = default;
                float best = float.MaxValue;
                foreach (var o in field.Discoveries())
                {
                    if (field.Library.Objects[o.Def].Parts[0].Shape != ObjectPart.WolfDen) continue;
                    float score = new Vector2(o.X - home.x, o.Z - home.z).magnitude;
                    if (score < best) { best = score; pick = o; }
                }
                if (best == float.MaxValue) return false;
                var q = new Quaternion(pick.Qx, pick.Qy, pick.Qz, pick.Qw);
                var centre = new Vector3(pick.X, pick.Y, pick.Z);
                float u = Structures.WolfU * pick.Size;
                var front = q * Vector3.forward;
                var along = q * Vector3.right;
                var target = centre - front * 4f * u + Vector3.up * 3f * u;
                var eye = centre + (front * 0.85f + along * 0.5f).normalized * 24f * u + Vector3.up * 7f * u;
                var ground = w.EntityManager.CreateEntityQuery(typeof(Ground)).GetSingleton<Ground>();
                eye.y = Mathf.Max(eye.y, ground.HeightAt(eye.x, eye.z) + 1.6f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                GameStatus.Lines["~aim den-cliff"] = $"wolf den variant {pick.Variant} size {pick.Size:F1} m at {pick.X:F0},{pick.Y:F0},{pick.Z:F0}, back fault {field.DenBackFault(pick) ?? "none"}, eye {eye.x:F1},{eye.y:F1},{eye.z:F1}";
                return true;
            }
            return false;
        }

        static bool AimVentCluster(Camera cam)
        {
            foreach (var w in Unity.Entities.World.All)
            {
                var field = w.Name == GameSettings.ViewClient ? w.GetExistingSystemManaged<ObjectColumnsSystem>()?.Field : null;
                if (field == null) continue;
                var vents = new List<ObjectInstance>();
                foreach (var o in field.Discoveries()) if (field.IsVent(o)) vents.Add(o);
                List<ObjectInstance> group = null;
                float spread = float.MaxValue;
                foreach (var a in vents)
                {
                    var near = new List<ObjectInstance>();
                    float far = 0;
                    foreach (var b in vents)
                    {
                        float d = new Vector2(b.X - a.X, b.Z - a.Z).magnitude;
                        if (d <= ClusterReachM) { near.Add(b); far = Mathf.Max(far, d); }
                    }
                    if (near.Count < 2) continue;
                    if (group == null || near.Count > group.Count || (near.Count == group.Count && far < spread)) { group = near; spread = far; }
                }
                if (group == null) return false;
                var c = Vector3.zero;
                foreach (var o in group) c += new Vector3(o.X, o.Y, o.Z);
                c /= group.Count;
                float r = 0;
                foreach (var o in group) r = Mathf.Max(r, new Vector2(o.X - c.x, o.Z - c.z).magnitude);
                var ground = w.EntityManager.CreateEntityQuery(typeof(Ground)).GetSingleton<Ground>();
                float back = r + 40f, low = float.MaxValue;
                var outward = Vector3.forward;
                for (int k = 0; k < 8; k++)
                {
                    var d = Quaternion.Euler(0, k * 45f, 0) * Vector3.forward;
                    float h = ground.HeightAt(c.x + d.x * back, c.z + d.z * back);
                    if (h < low) { low = h; outward = d; }
                }
                var eye = c + outward * back + Vector3.up * 0.3f * back;
                eye.y = Mathf.Max(eye.y, ground.HeightAt(eye.x, eye.z) + 12f);
                foreach (var o in group)
                    if (field.VentKind(o) == ObjectField.Geyser)
                    {
                        Structures.GeyserCycle(field.VentSeed(o), out float interval, out float duration, out float offset);
                        TerrainViewSystem.ClockOverride = offset + 0.4 * duration;
                        break;
                    }
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(c - eye));
                var kinds = new System.Text.StringBuilder();
                foreach (var o in group) kinds.Append($" {field.Library.Objects[o.Def].Id}@{o.X:F0},{o.Z:F0}");
                GameStatus.Lines["~aim vent-cluster"] = $"{group.Count} vents within {r:F0} m of {c.x:F0},{c.z:F0}:{kinds}, eye {eye.x:F0},{eye.y:F0},{eye.z:F0}";
                return true;
            }
            return false;
        }
    }
}
