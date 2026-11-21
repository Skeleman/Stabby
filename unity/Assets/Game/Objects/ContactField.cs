using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Stabby.World;
using Unity.Entities;
using UnityEngine;

namespace Stabby.Game
{
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class ContactFieldSystem : SystemBase
    {
        public const int Res = 1024;
        public const float TexelM = 0.25f, RebuildM = 40f;

        sealed class Splat
        {
            public byte[] Data;
            public float X0, Z0;
            public int Columns;
            public long Ms;
        }

        Task<Splat> build;
        Texture2D texture;
        Vector2 center = new Vector2(float.MaxValue, float.MaxValue);
        int builds;

        protected override void OnDestroy()
        {
            Shader.SetGlobalVector("_StabbyContactUV", Vector4.zero);
            if (texture != null) UnityEngine.Object.Destroy(texture);
        }

        protected override void OnUpdate()
        {
            if (build != null)
            {
                if (!build.IsCompleted) return;
                if (build.IsFaulted) { GameStatus.Failure = build.Exception.ToString(); build = null; Enabled = false; return; }
                var s = build.Result;
                build = null;
                var sw = Stopwatch.StartNew();
                if (texture == null)
                {
                    texture = new Texture2D(Res, Res, TextureFormat.R8, false, true) { name = "Contact", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                    Shader.SetGlobalTexture("_StabbyContact", texture);
                }
                texture.LoadRawTextureData(s.Data);
                texture.Apply(false, false);
                float span = Res * TexelM;
                Shader.SetGlobalVector("_StabbyContactUV", new Vector4(1f / span, -s.X0 / span, -s.Z0 / span, 1));
                GameStatus.Lines[World.Name + "~contact"] = $"{World.Name}: ground contact build {++builds}: {s.Columns} columns around {s.X0 + 0.5f * span:F0},{s.Z0 + 0.5f * span:F0} in {s.Ms} ms, upload {sw.Elapsed.TotalMilliseconds:F1} ms";
                return;
            }
            var field = World.GetExistingSystemManaged<ObjectColumnsSystem>()?.Field;
            var cam = Camera.main;
            if (field == null || cam == null) return;
            var p = cam.transform.position;
            var at = new Vector2(p.x, p.z);
            if ((at - center).sqrMagnitude < RebuildM * RebuildM) return;
            center = at;
            build = Task.Run(() => Make(field, at.x, at.y));
        }

        static Splat Make(ObjectField field, float cx, float cz)
        {
            var sw = Stopwatch.StartNew();
            float span = Res * TexelM;
            float x0 = Mathf.Floor((cx - 0.5f * span) / TexelM) * TexelM, z0 = Mathf.Floor((cz - 0.5f * span) / TexelM) * TexelM;
            var data = new byte[Res * Res];
            var objects = new List<ObjectInstance>();
            var columns = new List<ObjectColumn>();
            float cm = field.ChunkMeters;
            int n = field.ChunksAcross;
            int ax = Math.Max(0, (int)Math.Floor((x0 - 2f) / cm)), bx = Math.Min(n - 1, (int)Math.Floor((x0 + span + 2f) / cm));
            int az = Math.Max(0, (int)Math.Floor((z0 - 2f) / cm)), bz = Math.Min(n - 1, (int)Math.Floor((z0 + span + 2f) / cm));
            int count = 0;
            for (int kz = az; kz <= bz; kz++)
                for (int kx = ax; kx <= bx; kx++)
                {
                    field.PlaceInto(kx, kz, objects);
                    foreach (var o in objects)
                    {
                        columns.Clear();
                        field.ColumnsOf(o, columns);
                        float low = o.Y + 0.35f + 0.1f * o.Size;
                        foreach (var c in columns)
                        {
                            if (c.Bottom > low) continue;
                            Stamp(data, x0, z0, c.X, c.Z, c.R);
                            count++;
                        }
                    }
                }
            return new Splat { Data = data, X0 = x0, Z0 = z0, Columns = count, Ms = sw.ElapsedMilliseconds };
        }

        static void Stamp(byte[] data, float x0, float z0, float x, float z, float r)
        {
            float pad = Mathf.Clamp(0.25f + 0.35f * r, 0.25f, 0.9f), outer = r + pad, soft = pad + 0.3f * r;
            int ia = Math.Max(0, (int)((x - outer - x0) / TexelM)), ib = Math.Min(Res - 1, (int)((x + outer - x0) / TexelM) + 1);
            int ja = Math.Max(0, (int)((z - outer - z0) / TexelM)), jb = Math.Min(Res - 1, (int)((z + outer - z0) / TexelM) + 1);
            for (int j = ja; j <= jb; j++)
            {
                float dz = z0 + (j + 0.5f) * TexelM - z;
                int row = j * Res;
                for (int i = ia; i <= ib; i++)
                {
                    float dx = x0 + (i + 0.5f) * TexelM - x;
                    float t = (outer - Mathf.Sqrt(dx * dx + dz * dz)) / soft;
                    if (t <= 0) continue;
                    t = t >= 1 ? 1 : t * t * (3 - 2 * t);
                    byte v = (byte)(t * 255f);
                    if (v > data[row + i]) data[row + i] = v;
                }
            }
        }
    }
}
