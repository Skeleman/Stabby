using UnityEngine;
using Stabby.World;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        static System.Collections.IEnumerator WaterTintTour(Camera cam, string dir)
        {
            ClientMapSystem sys = null;
            Ground ground = default;
            foreach (var w in Unity.Entities.World.All)
            {
                var s = w.GetExistingSystemManaged<ClientMapSystem>();
                var q = w.EntityManager.CreateEntityQuery(typeof(Ground));
                if (s?.Map == null || s.Materials == null || q.CalculateEntityCount() != 1) continue;
                sys = s;
                ground = q.GetSingleton<Ground>();
                break;
            }
            if (sys == null) { GameStatus.Lines["~watertint"] = "watertint: no map"; yield break; }
            var map = sys.Map;
            int n = map.Config.Size;
            float cm = map.Config.CellMeters;
            var slots = sys.Materials.TintSlots();
            var slotOf = new int[map.BiomeIds.Length];
            for (int b = 0; b < slotOf.Length; b++)
            {
                slotOf[b] = -1;
                for (int t = 0; t < slots.Length; t++)
                    if (sys.Materials.Liquids[slots[t]].Biomes.Contains(map.BiomeIds[b])) slotOf[b] = t;
            }
            int SlotAt(int i) => map.Biome[i] < 0 ? -2 : slotOf[map.Biome[i]];
            RiverSegment river = null;
            int cross = -1;
            float best = float.MinValue;
            foreach (var r in map.Water.Rivers)
                for (int k = 8; k < r.Cells.Count - 8; k++)
                {
                    int a = SlotAt(r.Cells[k - 1]), b = SlotAt(r.Cells[k]);
                    if (a == b || a == -2 || b == -2 || a < 0 && b < 0) continue;
                    float d = (new Vector2(r.Cells[k] % n, r.Cells[k] / n) * cm - new Vector2(home.x, home.z)).magnitude;
                    float score = Mathf.Log(1 + r.Discharge[k]) * 10 - d * 0.002f;
                    if (score > best) { best = score; river = r; cross = k; }
                }
            Lake lake = null;
            foreach (var l in map.Water.Lakes)
                if (l.Outlet >= 0 && map.Biome[l.Outlet] >= 0 && slotOf[map.Biome[l.Outlet]] >= 0 && (lake == null || l.Cells.Count > lake.Cells.Count)) lake = l;
            cam.fieldOfView = 60f;
            if (river != null)
            {
                int i0 = river.Cells[cross - 4], i1 = river.Cells[cross + 4], c = river.Cells[cross];
                var along = new Vector2(i1 % n - i0 % n, i1 / n - i0 / n).normalized;
                var mid = new Vector3(c % n * cm, 0, c / n * cm);
                mid.y = ground.HeightAt(mid.x, mid.z);
                var eye = mid - new Vector3(along.x, 0, along.y) * 220f + Vector3.up * 140f;
                eye.y = Mathf.Max(eye.y, ground.HeightAt(eye.x, eye.z) + 40f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(mid + new Vector3(along.x, 0, along.y) * 60f - eye));
                GameStatus.Lines["~watertint river"] = $"river {river.Id} crosses {map.BiomeIds[map.Biome[river.Cells[cross - 1]]]} -> {map.BiomeIds[map.Biome[c]]} at {mid.x:F0},{mid.z:F0}, mix {river.Mix[cross - 8].A:F2}/{river.Mix[cross - 8].B:F2}/{river.Mix[cross - 8].C:F2} -> {river.Mix[cross + 8].A:F2}/{river.Mix[cross + 8].B:F2}/{river.Mix[cross + 8].C:F2}";
                for (var e = TintPair(cam, dir, "watertint-river"); e.MoveNext();) yield return null;
            }
            else GameStatus.Lines["~watertint river"] = "no river crosses a tinted biome border";
            if (lake != null)
            {
                var mid = Vector2.zero;
                foreach (int i in lake.Cells) mid += new Vector2(i % n, i / n);
                mid = mid / lake.Cells.Count * cm;
                int edge = -1;
                float bestD = float.MaxValue;
                foreach (int i in lake.Cells)
                {
                    float d = (new Vector2(i % n, i / n) * cm - new Vector2(home.x, home.z)).sqrMagnitude;
                    if (d < bestD) { bestD = d; edge = i; }
                }
                var shore = new Vector2(edge % n, edge / n) * cm;
                var to = (mid - shore).sqrMagnitude > 1f ? (mid - shore).normalized : Vector2.right;
                var eye = new Vector3(shore.x - to.x * 40f, 0, shore.y - to.y * 40f);
                eye.y = Mathf.Max(ground.HeightAt(eye.x, eye.z), lake.Level) + 18f;
                var target = new Vector3(shore.x + to.x * 80f, lake.Level, shore.y + to.y * 80f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                GameStatus.Lines["~watertint lake"] = $"lake {lake.Id} ({lake.Cells.Count} cells) in {map.BiomeIds[map.Biome[lake.Outlet]]}, mix {lake.Mix.A:F2}/{lake.Mix.B:F2}/{lake.Mix.C:F2}";
                for (var e = TintPair(cam, dir, "watertint-lake"); e.MoveNext();) yield return null;
            }
            else GameStatus.Lines["~watertint lake"] = "no lake drains a tinted biome";
        }

        static System.Collections.IEnumerator TintPair(Camera cam, string dir, string name)
        {
            for (var s = Settle(240); s.MoveNext();) yield return null;
            var slots = Shader.GetGlobalVector("_StabbyTintSlot");
            Shader.SetGlobalVector("_StabbyTintSlot", Vector4.zero);
            Shot(cam, null, "warmup");
            Shot(cam, dir, name + "-before");
            Shader.SetGlobalVector("_StabbyTintSlot", slots);
            Shot(cam, null, "warmup");
            Shot(cam, dir, name);
        }
    }
}
