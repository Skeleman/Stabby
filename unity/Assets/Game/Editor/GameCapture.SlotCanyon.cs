using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float SlotHour = 12.5f, SlotMoon = 0.5f, SlotEyeM = 1.7f, SlotRimUpM = 12f, SlotRimSegs = 8f, SlotRimBackM = 6f, SlotRimDropM = 3f;

        static System.Collections.IEnumerator SlotCanyonTour(Camera cam, string dir)
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
            if (!found) { GameStatus.Lines["~slotcanyon"] = GameStatus.Failure = "slotcanyon: no ground"; yield break; }
            int at = DeepestSlot(ground, out var e, out int segments);
            if (at < 0) { GameStatus.Lines["~slotcanyon"] = GameStatus.Failure = "slotcanyon: none placed"; yield break; }
            float ex = e.Bx - e.Ax, ez = e.Bz - e.Az, len = Mathf.Sqrt(ex * ex + ez * ez), ux = ex / len, uz = ez / len;
            float cx = e.Ax + 0.5f * ex, cz = e.Az + 0.5f * ez, sway = e.SlotSway(cx, cz);
            cx += -uz * sway;
            cz += ux * sway;
            float floor = 0.5f * (e.Ay + e.By), depth = 0.5f * (e.P + e.T) - floor;
            cam.fieldOfView = 60f;
            for (var w = ShowHour(SlotHour, SlotMoon); w.MoveNext();) yield return null;
            var eye = new Vector3(cx, floor + SlotEyeM, cz);
            var look = eye + new Vector3(-ux * 4f, 7f, -uz * 4f);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
            for (var w = Settle(240); w.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            var sky = DayNightSystem.Shown;
            string info = $"slotcanyon: edit {at} of {segments} segments, half width {e.W:F2}, depth {depth:F1}, floor {floor:F1}, at {cx:F0},{cz:F0}, GV {WorldConfig.GeneratorVersion}, pinned hour {sky.Hour:F2} moon {sky.Phase:F2}";
            GameStatus.Lines["~slotcanyon"] = info;
            Shot(cam, dir, "slotcanyon-floor");
            var down = DownSlot(ground, at, e);
            float back = 0.5f * len + SlotRimBackM, bx = cx - ux * back, bz = cz - uz * back;
            eye = new Vector3(bx, Mathf.Max(e.P, e.T) + SlotRimUpM, bz);
            look = new Vector3(0.5f * (down.Ax + down.Bx), down.P - SlotRimDropM, 0.5f * (down.Az + down.Bz));
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
            for (var w = Settle(120); w.MoveNext();) yield return null;
            GameStatus.Lines["~slotcanyon"] = info + $", rim eye {eye.x:F0},{eye.y:F0},{eye.z:F0}";
            Shot(cam, dir, "slotcanyon-rim");
        }

        static FeatureEdit DownSlot(Ground ground, int at, FeatureEdit e)
        {
            ref var g = ref ground.Blob.Value;
            var d = e;
            for (int k = at + 1; k < g.Edits.Length && k - at <= SlotRimSegs; k++)
            {
                var c = g.Edits[k];
                if (c.Shape != FeatureEdit.SlotCanyon || c.S != e.S) break;
                d = c;
            }
            return d;
        }

        static int DeepestSlot(Ground ground, out FeatureEdit e, out int segments)
        {
            ref var g = ref ground.Blob.Value;
            int at = -1;
            float best = 0f;
            segments = 0;
            for (int k = 0; k < g.Edits.Length; k++)
            {
                var c = g.Edits[k];
                if (c.Shape != FeatureEdit.SlotCanyon) continue;
                segments++;
                float depth = 0.5f * (c.P + c.T - c.Ay - c.By);
                if (c.F <= 0f && depth > best) { best = depth; at = k; }
            }
            e = at < 0 ? default : g.Edits[at];
            return at;
        }
    }
}
