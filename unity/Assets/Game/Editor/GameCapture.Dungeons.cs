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
            float door = Structures.DungeonZ0, floor = Structures.DungeonFloorY, room = door + 6 * Structures.KitCellM;
            var lamp = new GameObject("dungeon-lamp").AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = new Color(1f, 0.72f, 0.45f);
            lamp.range = 12f;
            lamp.intensity = 5f;
            lamp.enabled = false;
            cam.fieldOfView = 60f;
            var poses = new[]
            {
                ("entrance", at(5f, 2.4f, door - 11f), at(0f, 1.2f, door), false),
                ("interior", at(-4.5f, floor + 1.7f, room - 4.5f), at(2f, floor + 1.2f, room + 4f), true),
            };
            foreach (var (name, eye, look, lit) in poses)
            {
                lamp.enabled = lit;
                lamp.transform.position = at(0f, floor + 3f, room);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
                for (var w = ShowHour(PinnedMidday); w.MoveNext();) yield return null;
                for (var w = Settle(60); w.MoveNext();) yield return null;
                Shot(cam, dir, "dungeons-" + name);
            }
            Object.Destroy(lamp.gameObject);
            GameStatus.Lines["~dungeons"] = $"dungeons: {count} placed, nearest variant {pick.Variant} at {pick.X:F0},{pick.Y:F0},{pick.Z:F0}, {Structures.DungeonLayout(ObjectMeshes.Key(field.Library.Objects[pick.Def].Id, 0, pick.Variant)).Count} pieces, hour {PinnedMidday}, warm lamp in the first room";
        }
    }
}
