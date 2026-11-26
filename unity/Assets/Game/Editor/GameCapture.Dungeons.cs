using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        static System.Collections.IEnumerator DungeonsTour(Camera cam, string dir)
        {
            GameStatus.HoldCamera = true;
            ObjectField field = null;
            foreach (var w in Unity.Entities.World.All)
                if (w.Name == GameSettings.ViewClient) field = w.GetExistingSystemManaged<ObjectColumnsSystem>()?.Field;
            if (field == null) { GameStatus.Lines["~dungeons"] = GameStatus.Failure = "dungeons: no object field"; yield break; }
            ObjectInstance pick = default;
            float best = float.MaxValue;
            int count = 0;
            foreach (var o in field.Discoveries())
            {
                if (field.Library.Objects[o.Def].Parts[0].Shape != ObjectPart.Dungeon) continue;
                count++;
                float score = new Vector2(o.X - home.x, o.Z - home.z).magnitude;
                if (score < best) { best = score; pick = o; }
            }
            if (count == 0) { GameStatus.Lines["~dungeons"] = GameStatus.Failure = "dungeons: none placed"; yield break; }
            var q = new Quaternion(pick.Qx, pick.Qy, pick.Qz, pick.Qw);
            var centre = new Vector3(pick.X, pick.Y, pick.Z);
            float s = pick.Size / Structures.DungeonM;
            System.Func<float, float, float, Vector3> at = (x, y, z) => centre + q * new Vector3(x, y, z) * s;
            float door = Structures.DungeonZ0, floor = Structures.DungeonFloorY;
            var key = ObjectMeshes.Key(field.Library.Objects[pick.Def].Id, 0, pick.Variant);
            var plan = Structures.DungeonLayout(key);
            var torches = new System.Collections.Generic.List<StructureLight>();
            field.LightsOf(pick, torches);
            var inv = Quaternion.Inverse(q);
            Vector3 roomAt = new Vector3(0f, floor, door + 6 * Structures.KitCellM), torch = roomAt + Vector3.forward;
            int rooms = 0, litRooms = 0;
            float nearest = float.MaxValue;
            foreach (var p in plan)
            {
                if (p.Shape != ObjectPart.DungeonRoom) continue;
                rooms++;
                bool lit = false;
                foreach (var t in torches)
                {
                    var lt = inv * (new Vector3(t.X, t.Y, t.Z) - centre) / s;
                    if (Mathf.Abs(lt.x - p.X) > 0.5f * Structures.RoomM || Mathf.Abs(lt.z - p.Z) > 0.5f * Structures.RoomM) continue;
                    lit = true;
                    float d = p.Z - door + Mathf.Abs(p.X);
                    if (d < nearest) { nearest = d; roomAt = new Vector3(p.X, floor, p.Z); torch = lt; }
                }
                if (lit) litRooms++;
            }
            var away = new Vector3(roomAt.x - torch.x, 0f, roomAt.z - torch.z).normalized;
            var eye = roomAt + away * 4f;
            cam.fieldOfView = 60f;
            var poses = new[]
            {
                ("entrance", at(5f, 2.4f, door - 11f), at(0f, 1.2f, door)),
                ("interior", at(eye.x, floor + 1.7f, eye.z), at(torch.x, torch.y - 0.6f, torch.z)),
            };
            foreach (var (name, from, look) in poses)
            {
                cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(look - from));
                for (var w = ShowHour(PinnedMidday, SkyDef.FullMoon); w.MoveNext();) yield return null;
                for (var w = Settle(60); w.MoveNext();) yield return null;
                Shot(cam, dir, "dungeons-" + name);
            }
            GameStatus.Lines["~dungeons"] = $"dungeons: {count} placed, nearest variant {pick.Variant} at {pick.X:F0},{pick.Y:F0},{pick.Z:F0}, {plan.Count} pieces, {litRooms} of {rooms} rooms torch-lit, {torches.Count} torches, interior faces the nearest lit room at {roomAt.x:F0},{roomAt.z:F0}, hour {PinnedMidday}, full moon, no capture lamp";
        }
    }
}
