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
        public const float RebuildShare = 0.15f;

        sealed class Splat
        {
            public byte[] Data;
            public float X0, Z0, TexelM;
            public int Columns;
            public long Ms;
        }

        Task<Splat> build;
        Texture2D texture;
        Vector2 center = new Vector2(float.MaxValue, float.MaxValue);
        float radius;
        int builds, removedSeen;

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
                float span = Res * s.TexelM;
                Shader.SetGlobalVector("_StabbyContactUV", new Vector4(1f / span, -s.X0 / span, -s.Z0 / span, 1));
                GameStatus.Lines[World.Name + "~contact"] = $"{World.Name}: ground contact build {++builds}: {s.Columns} columns, {span:F0} m window at {s.TexelM:F2} m texels around {s.X0 + 0.5f * span:F0},{s.Z0 + 0.5f * span:F0} in {s.Ms} ms, upload {sw.Elapsed.TotalMilliseconds:F1} ms";
                return;
            }
            var columns = World.GetExistingSystemManaged<ObjectColumnsSystem>();
            var field = columns?.Field;
            var cam = Camera.main;
            if (field == null || cam == null) return;
            var view = DetailView.Refresh(cam);
            columns.Library.Terrain.ContactWindow(view.PixelsPerMetre, view.Quality, Res, out float r, out float texel);
            var deltas = World.GetExistingSystemManaged<ClientObjectDeltaSystem>()?.Deltas;
            int removedNow = deltas?.Count ?? 0;
            var p = cam.transform.position;
            var at = new Vector2(p.x, p.z);
            float move = RebuildShare * radius;
            bool moved = (at - center).sqrMagnitude >= move * move, rescaled = Mathf.Abs(r - radius) > RebuildShare * radius;
            if (!moved && !rescaled && removedNow == removedSeen) return;
            center = at;
            radius = r;
            removedSeen = removedNow;
            var gone = new HashSet<ulong>();
            if (deltas != null)
                foreach (int c in deltas.Chunks)
                    foreach (ulong id in deltas.InChunk(c)) gone.Add(id);
            build = Task.Run(() => Make(field, gone, at.x, at.y, texel));
        }

        static Splat Make(ObjectField field, HashSet<ulong> gone, float cx, float cz, float texelM)
        {
            var sw = Stopwatch.StartNew();
            float span = Res * texelM;
            float x0 = Mathf.Floor((cx - 0.5f * span) / texelM) * texelM, z0 = Mathf.Floor((cz - 0.5f * span) / texelM) * texelM;
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
                        if (gone.Contains(o.Id)) continue;
                        columns.Clear();
                        field.ColumnsOf(o, columns);
                        float low = o.Y + 0.35f + 0.1f * o.Size;
                        float foot = field.Library.Objects[o.Def].Kind == ObjectLibrary.Structure ? Structures.FootReach * o.Size : 0f;
                        foreach (var c in columns)
                        {
                            if (c.Bottom > low) continue;
                            Stamp(data, x0, z0, texelM, c.X, c.Z, Math.Max(c.R, foot));
                            count++;
                        }
                    }
                }
            return new Splat { Data = data, X0 = x0, Z0 = z0, TexelM = texelM, Columns = count, Ms = sw.ElapsedMilliseconds };
        }

        static void Stamp(byte[] data, float x0, float z0, float texelM, float x, float z, float r)
        {
            float pad = Mathf.Clamp(0.25f + 0.35f * r, 0.25f, 0.9f), outer = r + pad, soft = pad + 0.3f * r;
            int ia = Math.Max(0, (int)((x - outer - x0) / texelM)), ib = Math.Min(Res - 1, (int)((x + outer - x0) / texelM) + 1);
            int ja = Math.Max(0, (int)((z - outer - z0) / texelM)), jb = Math.Min(Res - 1, (int)((z + outer - z0) / texelM) + 1);
            for (int j = ja; j <= jb; j++)
            {
                float dz = z0 + (j + 0.5f) * texelM - z;
                int row = j * Res;
                for (int i = ia; i <= ib; i++)
                {
                    float dx = x0 + (i + 0.5f) * texelM - x;
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
