using System.Collections.Generic;
using Stabby.World;
using Unity.Entities;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float FirelightHour = 23f, FirelightBack = 8f;

        static bool CampFire(ObjectField field, out ObjectInstance pick, out Vector3 fire, out Vector3 eye, out string why)
        {
            pick = default;
            fire = eye = default;
            why = null;
            float best = float.MaxValue;
            foreach (var o in field.Discoveries())
            {
                if (field.Library.Objects[o.Def].Parts[0].Shape != ObjectPart.Campsite) continue;
                float d = new Vector2(o.X - home.x, o.Z - home.z).magnitude;
                if (d < best) { best = d; pick = o; }
            }
            if (best == float.MaxValue) { why = "firelight: no campsite"; return false; }
            var anchors = new List<StructureLight>();
            field.LightsOf(pick, anchors);
            if (anchors.Count == 0) { why = "firelight: campsite has no fire anchor"; return false; }
            fire = new Vector3(anchors[0].X, anchors[0].Y, anchors[0].Z);
            var q = new Quaternion(pick.Qx, pick.Qy, pick.Qz, pick.Qw);
            float flattest = float.MaxValue;
            for (int a = 0; a < 16; a++)
            {
                var d = Quaternion.Euler(0, a * 22.5f, 0) * (q * Vector3.forward);
                var p = fire + d * FirelightBack;
                float h = GroundHeight(p.x, p.z), rough = Mathf.Abs(h - fire.y);
                for (float r = 2f; r < FirelightBack; r += 2f) rough += Mathf.Max(0f, GroundHeight(fire.x + d.x * r, fire.z + d.z * r) - fire.y - 0.5f) * 4f;
                if (rough < flattest) { flattest = rough; eye = new Vector3(p.x, h + 1.7f, p.z); }
            }
            return true;
        }

        static System.Collections.IEnumerator FirelightTour(Camera cam, string dir)
        {
            GameStatus.HoldCamera = true;
            cam.fieldOfView = 55f;
            var lines = new List<string>();
            var world = ViewWorld();
            if (world == null) { GameStatus.Failure = "firelight: no client view world"; yield break; }
            var em = world.EntityManager;
            var field = world.GetExistingSystemManaged<ObjectColumnsSystem>()?.Field;
            for (int k = 0; k < 1800 && field == null; k++) { yield return null; field = world.GetExistingSystemManaged<ObjectColumnsSystem>()?.Field; }
            if (field == null) { GameStatus.Failure = "firelight: no object field"; yield break; }
            if (!CampFire(field, out var pick, out var fire, out var eye, out var why)) { GameStatus.Failure = why; yield break; }
            var aim = fire + Vector3.up * 0.4f;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim - eye));
            for (var w = ShowHour(FirelightHour, 0f); w.MoveNext();) yield return null;
            for (var w = Settle(360); w.MoveNext();) yield return null;
            for (int k = 0; k < 1800 && ObjectViewSystem.Pending > 0; k++) yield return null;
            var stateQuery = em.CreateEntityQuery(typeof(LightBudgetState));
            var emitters = em.CreateEntityQuery(typeof(LightEmitter));
            for (int k = 0; k < 600 && (stateQuery.IsEmpty || emitters.IsEmpty); k++) yield return null;
            if (stateQuery.IsEmpty) { GameStatus.Failure = "firelight: LightPresenterSystem never created the budget"; yield break; }
            if (emitters.IsEmpty) { GameStatus.Failure = "firelight: no fire emitters spawned"; yield break; }
            var presenter = world.GetExistingSystemManaged<LightPresenterSystem>();
            for (var w = Settle(120); w.MoveNext();) yield return null;
            var s = DayNightSystem.Shown;
            lines.Add($"hour {s.Hour:F2}, moon phase {s.Phase:F2}: {(s.Moon ? "moon" : "sun")} light {s.Intensity:F4}, ambient sky {s.AmbientSky.B:F4}; campsite variant {pick.Variant} size {pick.Size:F1} at {pick.X:F0},{pick.Y:F0},{pick.Z:F0}, fire {fire.x:F1},{fire.y:F1},{fire.z:F1}, eye {(eye - fire).magnitude:F1} m; fire emitters {emitters.CalculateEntityCount()}");
            for (var w = SetLights(stateQuery, presenter, true, true); w.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            Shot(cam, dir, "firelight-off");
            double off = BenchAt(cam, 1920, 1080);
            for (var w = SetLights(stateQuery, presenter, false, true); w.MoveNext();) yield return null;
            lines.Add("lit: " + TierLine(stateQuery, presenter));
            Shot(cam, null, "warmup");
            Shot(cam, dir, "firelight-on");
            double on = BenchAt(cam, 1920, 1080);
            lines.Add($"bench 1080p ms: fires off {off:F2}, fires on {on:F2}");
            GameStatus.Lines["~firelight"] = string.Join("\n", lines);
        }
    }
}
