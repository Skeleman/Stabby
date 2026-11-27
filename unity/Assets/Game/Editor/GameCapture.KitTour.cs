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
        const float KitMinRadius = 0.35f, KitTallM = 2f, KitCanopy = 0.4f;
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

        static bool KitInWorld(ObjectDef def)
        {
            int kind = def.Placement.Discovery;
            return ObjectField.Pooled(kind) || kind == ObjectField.WolfDen || kind == ObjectField.FairyRing;
        }

        static System.Collections.IEnumerator KitTour(Camera cam, string dir)
        {
            GameStatus.HoldCamera = true;
            ObjectLibrary lib = null;
            ObjectField field = null;
            GeneratedWorld map = null;
            Ground ground = default;
            bool found = false;
            foreach (var w in Unity.Entities.World.All)
            {
                if (w.Name != GameSettings.ViewClient) continue;
                field = w.GetExistingSystemManaged<ObjectColumnsSystem>()?.Field;
                lib = field?.Library;
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
            var sites = new List<(string id, int def)>();
            int tris = 0;
            for (int di = 0; di < lib.Objects.Count; di++)
            {
                var def = lib.Objects[di];
                if (!KitUser(def)) continue;
                if (KitInWorld(def)) { sites.Add((def.Id, di)); continue; }
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
            var tall = KitOccluders(field, map, start, length);
            cam.fieldOfView = KitFov;
            for (var w = ShowHour(KitHour, KitMoon); w.MoveNext();) yield return null;
            var mid = new Vector3(start.x + 0.5f * length, 0.5f * (lo + hi), start.y);
            var rowEye = mid + new Vector3(0, 0.22f * length, -0.62f * length);
            cam.transform.SetPositionAndRotation(rowEye, Quaternion.LookRotation(mid - rowEye));
            for (var w = Settle(120); w.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            Shot(cam, dir, "kit-row");
            int cells = placed.Count + sites.Count, rows = (cells + KitCols - 1) / KitCols;
            var sheet = new Texture2D(KitCellW * KitCols, KitCellH * rows, TextureFormat.RGB24, false);
            var fill = new Color32[sheet.width * sheet.height];
            for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(24, 24, 24, 255);
            sheet.SetPixels32(fill);
            var cell = new RenderTexture(KitCellW, KitCellH, 24);
            var order = new System.Text.StringBuilder();
            float half = Mathf.Sin(0.5f * KitFov * Mathf.Deg2Rad);
            int moved = 0, blocked = 0;
            var placedSites = field.Discoveries();
            int missing = 0;
            for (int k = 0; k < cells; k++)
            {
                string id;
                Vector3 at;
                float rad;
                var near = tall;
                if (k < placed.Count)
                {
                    Bounds b;
                    (id, b) = placed[k];
                    at = b.center;
                    rad = Mathf.Max(KitMinRadius, b.extents.magnitude);
                }
                else
                {
                    int def;
                    (id, def) = sites[k - placed.Count];
                    float best = float.MaxValue;
                    ObjectInstance pick = default;
                    foreach (var o in placedSites)
                    {
                        float d = new Vector2(o.X - home.x, o.Z - home.z).sqrMagnitude;
                        if (o.Def == def && d < best) { best = d; pick = o; }
                    }
                    if (best == float.MaxValue) { missing++; continue; }
                    rad = 0.45f * pick.Size;
                    at = new Vector3(pick.X, Mathf.Max(pick.Y, ground.HeightAt(pick.X, pick.Z)) + 0.15f * rad, pick.Z);
                    near = KitOccluders(field, map, new Vector2(pick.X - rad, pick.Z), 2 * rad);
                }
                var eye = KitEye(ground, near, at, rad / half, out int view, out int hits);
                if (view != 0) moved++;
                if (hits > 0) blocked++;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
                for (var w = Settle(k < placed.Count ? 20 : 180); w.MoveNext();) yield return null;
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
            GameStatus.Lines["~kit"] = $"kit: {placed.Count} kit and timber users in a {length:F0} m row, {sites.Count - missing} edit sites shot in the world ({missing} not placed), from {start.x:F0},{start.y:F0}, ground {lo:F1}-{hi:F1} m, {tall.Count} tall world objects, {moved} eyes moved off canopy, {blocked} still blocked, {tris} near triangles, hour {sky.Hour:F2} moon {sky.Phase:F2}, sheet {KitCols}x{rows}";
        }

        static List<Vector4> KitOccluders(ObjectField field, GeneratedWorld map, Vector2 start, float length)
        {
            var tall = new List<Vector4>();
            var list = new List<ObjectInstance>();
            float cm = field.ChunkMeters, pad = 40f;
            int across = WorldChunks.Across(map.Config.Size);
            int x0 = Mathf.Max(0, (int)((start.x - pad) / cm)), x1 = Mathf.Min(across - 1, (int)((start.x + length + pad) / cm));
            int z0 = Mathf.Max(0, (int)((start.y - pad) / cm)), z1 = Mathf.Min(across - 1, (int)((start.y + pad) / cm));
            for (int cz = z0; cz <= z1; cz++)
                for (int cx = x0; cx <= x1; cx++)
                {
                    field.PlaceInto(cx, cz, list);
                    foreach (var o in list)
                        if (o.Size >= KitTallM) tall.Add(new Vector4(o.X, o.Y, o.Z, o.Size));
                }
            return tall;
        }

        static readonly Vector3[] KitLooks =
        {
            new Vector3(-0.55f, 0.42f, -0.72f), new Vector3(0.55f, 0.42f, -0.72f), new Vector3(-0.72f, 0.42f, 0.55f), new Vector3(0.72f, 0.42f, 0.55f),
            new Vector3(0, 0.42f, -0.9f), new Vector3(-0.3f, 0.75f, -0.6f), new Vector3(0.3f, 0.75f, -0.6f), new Vector3(0, 0.95f, -0.3f),
        };

        static Vector3 KitEye(Ground ground, List<Vector4> tall, Vector3 at, float dist, out int view, out int hits)
        {
            view = 0; hits = int.MaxValue;
            var best = at;
            for (int k = 0; k < KitLooks.Length && hits > 0; k++)
            {
                var eye = at + KitLooks[k].normalized * dist;
                eye.y = Mathf.Max(eye.y, ground.HeightAt(eye.x, eye.z) + Mathf.Min(1.5f, 0.3f + 0.5f * dist));
                int h = 0;
                for (int s = 0; s <= 16; s++)
                {
                    var p = Vector3.Lerp(eye, at, s / 20f);
                    foreach (var t in tall)
                    {
                        float dx = p.x - t.x, dz = p.z - t.z, r = KitCanopy * t.w;
                        if (dx * dx + dz * dz < r * r && p.y > t.y && p.y < t.y + t.w) { h++; break; }
                    }
                }
                if (h < hits) { hits = h; view = k; best = eye; }
            }
            return best;
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
