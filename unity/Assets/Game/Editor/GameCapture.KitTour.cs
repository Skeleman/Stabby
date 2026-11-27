using System.Collections.Generic;
using System.IO;
using Stabby.World;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float KitHour = 12f, KitMoon = 0.5f, KitGapM = 6f, KitFov = 50f;
        const int KitCellW = 640, KitCellH = 360, KitCols = 7;

        static bool KitUser(ObjectDef def)
        {
            foreach (var p in def.Parts)
            {
                if (p.Shape == ObjectPart.Dungeon) return false;
                if (p.Shape == ObjectPart.Log || p.Shape == ObjectPart.Stump || ObjectPart.IsStructure(p.Shape)) return true;
            }
            return false;
        }

        static System.Collections.IEnumerator KitTour(Camera cam, string dir)
        {
            GameStatus.HoldCamera = true;
            ObjectLibrary lib = null;
            GeneratedWorld map = null;
            Ground ground = default;
            bool found = false;
            foreach (var w in Unity.Entities.World.All)
            {
                if (w.Name != GameSettings.ViewClient) continue;
                lib = w.GetExistingSystemManaged<ObjectColumnsSystem>()?.Field?.Library;
                map = w.GetExistingSystemManaged<ClientMapSystem>()?.Map;
                var q = w.EntityManager.CreateEntityQuery(typeof(Ground));
                if (q.CalculateEntityCount() == 1) { ground = q.GetSingleton<Ground>(); found = true; }
            }
            if (lib == null || map == null || !found) { GameStatus.Lines["~kit"] = GameStatus.Failure = "kit: no object library, map or ground"; yield break; }
            var colorOf = typeof(ObjectViewSystem).GetMethod("ColorBase", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (colorOf == null) { GameStatus.Lines["~kit"] = GameStatus.Failure = "kit: ObjectViewSystem.ColorBase not found"; yield break; }
            var shader = Shader.Find("Stabby/Object");
            var root = new GameObject("kit-row");
            var items = new List<(string id, GameObject go, Bounds b, float size)>();
            int tris = 0;
            foreach (var def in lib.Objects)
            {
                if (!KitUser(def)) continue;
                float size = 0.5f * (def.Placement.SizeMinM + def.Placement.SizeMaxM);
                var go = new GameObject(def.Id);
                go.transform.SetParent(root.transform, false);
                for (int p = 0; p < def.Parts.Count; p++)
                {
                    var part = def.Parts[p];
                    var m = ObjectMeshes.Build(part, ObjectMeshes.Key(def.Id, p, 0), 0);
                    tris += m.TriangleCount;
                    var pg = new GameObject(def.Id + "-" + p);
                    pg.transform.SetParent(go.transform, false);
                    pg.transform.localPosition = new Vector3(part.OffsetX, part.OffsetY, part.OffsetZ) * size;
                    pg.transform.localScale = new Vector3(part.ScaleX, part.ScaleY, part.ScaleZ) * size;
                    pg.AddComponent<MeshFilter>().sharedMesh = DungeonMesh(m);
                    var mat = new Material(shader);
                    var c = (float4)colorOf.Invoke(null, new object[] { part });
                    mat.SetColor("_BaseColor", new Color(c.x, c.y, c.z, c.w));
                    var r = pg.AddComponent<MeshRenderer>();
                    r.sharedMaterial = mat;
                    r.shadowCastingMode = ShadowCastingMode.On;
                }
                var b = new Bounds(go.transform.position, Vector3.zero);
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>()) b.Encapsulate(r.bounds);
                items.Add((def.Id, go, b, size));
            }
            float length = 0;
            foreach (var it in items) length += it.b.size.x + KitGapM;
            if (!KitSite(ground, map, length, out var start)) { Object.Destroy(root); GameStatus.Lines["~kit"] = GameStatus.Failure = $"kit: no dry flat strip {length:F0} m long"; yield break; }
            float x = start.x;
            float lo = float.MaxValue, hi = float.MinValue;
            var placed = new List<(string id, Bounds b)>();
            foreach (var (id, go, b, size) in items)
            {
                float ox = x + KitGapM * 0.5f + (go.transform.position.x - b.min.x);
                float oz = start.y - b.center.z;
                float gy = ground.HeightAt(ox, oz);
                lo = Mathf.Min(lo, gy); hi = Mathf.Max(hi, gy);
                go.transform.position = new Vector3(ox, gy, oz);
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                {
                    r.sharedMaterial.SetVector("_Fade", new Vector4(1, 1, gy, size));
                }
                var wb = new Bounds(go.transform.position, Vector3.zero);
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>()) wb.Encapsulate(r.bounds);
                placed.Add((id, wb));
                x += b.size.x + KitGapM;
            }
            cam.fieldOfView = KitFov;
            for (var w = ShowHour(KitHour, KitMoon); w.MoveNext();) yield return null;
            var mid = new Vector3(start.x + 0.5f * length, 0.5f * (lo + hi), start.y);
            var rowEye = mid + new Vector3(0, 0.22f * length, -0.62f * length);
            cam.transform.SetPositionAndRotation(rowEye, Quaternion.LookRotation(mid - rowEye));
            for (var w = Settle(120); w.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            Shot(cam, dir, "kit-row");
            int rows = (placed.Count + KitCols - 1) / KitCols;
            var sheet = new Texture2D(KitCellW * KitCols, KitCellH * rows, TextureFormat.RGB24, false);
            var fill = new Color32[sheet.width * sheet.height];
            for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(24, 24, 24, 255);
            sheet.SetPixels32(fill);
            var cell = new RenderTexture(KitCellW, KitCellH, 24);
            var order = new System.Text.StringBuilder();
            var look = new Vector3(-0.55f, 0.42f, -0.72f).normalized;
            float half = Mathf.Sin(0.5f * KitFov * Mathf.Deg2Rad);
            for (int k = 0; k < placed.Count; k++)
            {
                var (id, b) = placed[k];
                float rad = Mathf.Max(1.2f, b.extents.magnitude);
                var eye = b.center + look * (rad / half);
                eye.y = Mathf.Max(eye.y, ground.HeightAt(eye.x, eye.z) + 1.5f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(b.center - eye));
                for (var w = Settle(20); w.MoveNext();) yield return null;
                Shot(cam, dir, "kit-" + id);
                cam.targetTexture = cell;
                cam.Render();
                RenderTexture.active = cell;
                int col = k % KitCols, row = rows - 1 - k / KitCols;
                sheet.ReadPixels(new Rect(0, 0, KitCellW, KitCellH), col * KitCellW, row * KitCellH);
                RenderTexture.active = null;
                cam.targetTexture = null;
                order.Append(k / KitCols).Append(',').Append(col).Append(' ').Append(id).Append('\n');
            }
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(dir, "kit-sheet.png"), sheet.EncodeToPNG());
            File.WriteAllText(Path.Combine(dir, "kit-sheet.txt"), order.ToString());
            Object.DestroyImmediate(cell);
            Object.DestroyImmediate(sheet);
            Object.Destroy(root);
            var sky = DayNightSystem.Shown;
            GameStatus.Lines["~kit"] = $"kit: {placed.Count} kit and timber users in a {length:F0} m row from {start.x:F0},{start.y:F0}, ground {lo:F1}-{hi:F1} m, {tris} near triangles, hour {sky.Hour:F2} moon {sky.Phase:F2}, sheet {KitCols}x{rows}";
        }

        static bool KitSite(Ground ground, GeneratedWorld map, float length, out Vector2 start)
        {
            const float Step = 64f, Span = 1600f, Sample = 8f;
            start = default;
            int n = map.Config.Size;
            float cm = map.Config.CellMeters, best = float.MaxValue;
            for (float dz = -Span; dz <= Span; dz += Step)
                for (float dx = -Span; dx <= Span; dx += Step)
                {
                    float x0 = home.x + dx - 0.5f * length, z = home.z + dz, lo = float.MaxValue, hi = float.MinValue;
                    bool dry = true;
                    for (float t = 0; t <= length && dry; t += Sample)
                        for (float side = -12f; side <= 12f && dry; side += 12f)
                        {
                            float sx = x0 + t, sz = z + side;
                            int cx = (int)(sx / cm), cz = (int)(sz / cm);
                            if (cx < 0 || cz < 0 || cx >= n || cz >= n) { dry = false; break; }
                            int i = cz * n + cx;
                            float h = ground.HeightAt(sx, sz);
                            if (h < 3f || (map.Water.LakeId != null && map.Water.LakeId[i] >= 0) || (map.Water.IsRiver != null && map.Water.IsRiver[i])) { dry = false; break; }
                            lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                        }
                    if (!dry) continue;
                    float score = hi - lo + 0.001f * Mathf.Sqrt(dx * dx + dz * dz);
                    if (score >= best) continue;
                    best = score;
                    start = new Vector2(x0, z);
                }
            return best < float.MaxValue;
        }
    }
}
