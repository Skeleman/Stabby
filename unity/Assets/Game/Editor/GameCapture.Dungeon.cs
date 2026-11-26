using System.Collections.Generic;
using Stabby.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        static readonly (string id, float y, float z)[] DungeonChain = { ("crypt-room", 0, 0), ("crypt-corridor", 0, 8), ("crypt-stairs", 0, 14), ("crypt-room", 4, 24) };

        static System.Collections.IEnumerator DungeonTour(Camera cam, string dir)
        {
            GameStatus.HoldCamera = true;
            ObjectLibrary lib = null;
            Ground ground = default;
            bool found = false;
            foreach (var w in Unity.Entities.World.All)
            {
                if (w.Name != GameSettings.ViewClient) continue;
                lib = w.GetExistingSystemManaged<ObjectColumnsSystem>()?.Field?.Library;
                var q = w.EntityManager.CreateEntityQuery(typeof(Ground));
                if (q.CalculateEntityCount() == 1) { ground = q.GetSingleton<Ground>(); found = true; }
            }
            if (lib == null || !found) { GameStatus.Lines["~dungeon"] = GameStatus.Failure = "dungeon: no object library or ground"; yield break; }
            var shader = Shader.Find("Stabby/Object");
            var root = new GameObject("dungeon-chain");
            float gx = home.x + 30f, gz = home.z + 30f, high = float.MinValue;
            for (float x = -8; x <= 8; x += 4)
                for (float z = -8; z <= 32; z += 4) high = Mathf.Max(high, ground.HeightAt(gx + x, gz + z));
            var origin = new Vector3(gx, high + 30f, gz);
            root.transform.position = origin;
            int tris = 0;
            foreach (var (id, y, z) in DungeonChain)
            {
                int d = lib.Objects.FindIndex(o => o.Id == id);
                if (d < 0) { GameStatus.Lines["~dungeon"] = GameStatus.Failure = "dungeon: missing " + id; Object.Destroy(root); yield break; }
                var def = lib.Objects[d];
                var m = ObjectMeshes.Build(def.Parts[0], ObjectMeshes.Key(def.Id, 0, 0), 0);
                tris += m.TriangleCount;
                var go = new GameObject(id);
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = new Vector3(0, y, z);
                go.transform.localScale = Vector3.one * def.Placement.SizeMinM;
                go.AddComponent<MeshFilter>().sharedMesh = DungeonMesh(m);
                var mat = new Material(shader);
                mat.SetColor("_BaseColor", new Color(1, 1, 1, 0));
                mat.SetVector("_Fade", new Vector4(1, 1, 0, 0));
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.On;
                var lamp = new GameObject(id + "-lamp").AddComponent<Light>();
                lamp.transform.SetParent(root.transform, false);
                lamp.transform.localPosition = new Vector3(0, y + 2.2f, z);
                lamp.type = LightType.Point;
                lamp.color = new Color(1f, 0.72f, 0.45f);
                lamp.range = 9f;
                lamp.intensity = 4f;
            }
            cam.fieldOfView = 60f;
            var poses = new[]
            {
                ("chain", origin + new Vector3(-26f, 22f, -14f), origin + new Vector3(0, 2f, 12f)),
                ("corridor", origin + new Vector3(0, 1.6f, 3.5f), origin + new Vector3(0, 2.2f, 14f)),
                ("stairs", origin + new Vector3(0, 1.6f, 7.5f), origin + new Vector3(0, 5.2f, 18f)),
            };
            foreach (var (name, eye, at) in poses)
            {
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
                for (var w = ShowHour(PinnedMidday); w.MoveNext();) yield return null;
                for (var w = Settle(30); w.MoveNext();) yield return null;
                Shot(cam, dir, "dungeon-" + name);
            }
            Object.Destroy(root);
            GameStatus.Lines["~dungeon"] = $"dungeon: crypt room-corridor-stairs-room, {tris} near triangles, hour {PinnedMidday}, origin {origin.x:F0},{origin.y:F0},{origin.z:F0} 30 m above ground, warm point lamp per module";
        }

        static Mesh DungeonMesh(ObjectMesh m)
        {
            int vc = m.VertexCount;
            var pos = new Vector3[vc];
            var nrm = new Vector3[vc];
            var tones = new List<Vector4>(vc);
            Vector3 mid = Vector3.zero;
            for (int v = 0; v < vc; v++)
            {
                pos[v] = new Vector3(m.Positions[3 * v], m.Positions[3 * v + 1], m.Positions[3 * v + 2]);
                nrm[v] = new Vector3(m.Normals[3 * v], m.Normals[3 * v + 1], m.Normals[3 * v + 2]);
                tones.Add(m.Tones != null ? new Vector4(m.Tones[4 * v], m.Tones[4 * v + 1], m.Tones[4 * v + 2], m.Tones[4 * v + 3]) : Vector4.zero);
                mid += pos[v];
            }
            mid /= Mathf.Max(1, vc);
            float outward = 0;
            for (int v = 0; v < vc; v++) outward += Vector3.Dot(nrm[v], pos[v] - mid);
            var idx = (int[])m.Indices.Clone();
            if (outward < 0)
            {
                for (int t = 0; t < idx.Length; t += 3) (idx[t + 1], idx[t + 2]) = (idx[t + 2], idx[t + 1]);
                for (int v = 0; v < vc; v++) nrm[v] = -nrm[v];
            }
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.vertices = pos;
            mesh.normals = nrm;
            mesh.SetUVs(0, tones);
            mesh.SetIndices(idx, MeshTopology.Triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
