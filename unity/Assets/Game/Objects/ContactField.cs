using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Stabby.World;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;

namespace Stabby.Game
{
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class ContactFieldSystem : SystemBase
    {
        public const int Res = 1024;
        public const float RebuildShare = 0.15f;

        NativeHashMap<int, ContactChunk> chunks;
        NativeHashSet<ulong> gone;
        NativeArray<byte> data;
        NativeReference<int> columnCount;
        Task<List<(int chunk, ContactChunk table)>> converting;
        JobSlot splat;
        ContactWindow window, pending;
        Texture2D texture;
        readonly List<int> missing = new List<int>(), evict = new List<int>();
        readonly Stopwatch clock = new Stopwatch();
        Vector2 center = new Vector2(float.MaxValue, float.MaxValue);
        float radius;
        int builds, removedSeen, across;
        long convertMs;

        public bool Built => builds > 0 && !splat.Busy && converting == null;
        public ContactWindow Window => window;
        public NativeArray<byte> Data => data;
        public int Columns => columnCount.IsCreated ? columnCount.Value : 0;
        public int Tables => chunks.IsCreated ? chunks.Count : 0;

        protected override void OnCreate()
        {
            chunks = new NativeHashMap<int, ContactChunk>(64, Allocator.Persistent);
            gone = new NativeHashSet<ulong>(64, Allocator.Persistent);
            data = new NativeArray<byte>(Res * Res * 2, Allocator.Persistent);
            columnCount = new NativeReference<int>(Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            splat.Finish();
            Owned.Release(converting, made => { foreach (var (_, t) in made) t.Dispose(); });
            foreach (var kv in chunks) kv.Value.Dispose();
            chunks.Dispose();
            gone.Dispose();
            data.Dispose();
            columnCount.Dispose();
            Shader.SetGlobalVector("_StabbyContactUV", Vector4.zero);
            if (texture != null) UnityEngine.Object.Destroy(texture);
        }

        protected override void OnUpdate()
        {
            if (splat.Busy)
            {
                if (!splat.Done) return;
                splat.Finish();
                Upload();
                return;
            }
            var columns = World.GetExistingSystemManaged<ObjectColumnsSystem>();
            var field = columns?.Field;
            if (converting != null)
            {
                if (!converting.IsCompleted) return;
                if (converting.IsFaulted) { GameStatus.Failure = converting.Exception.ToString(); converting = null; Enabled = false; return; }
                foreach (var (c, t) in converting.Result) chunks[c] = t;
                converting = null;
                convertMs = clock.ElapsedMilliseconds;
                Schedule(pending);
                return;
            }
            var cam = Camera.main;
            if (field == null || cam == null) return;
            var view = DetailView.Refresh(cam);
            columns.Library.Terrain.ContactWindow(view.PixelsPerMetre, view.Quality, Res, out float r, out float texel);
            var tables = World.GetExistingSystemManaged<ClientObjectDeltaSystem>()?.Tables;
            int removedNow = tables?.RemovedOrder.Length ?? 0;
            var p = cam.transform.position;
            var at = new Vector2(p.x, p.z);
            float move = RebuildShare * radius;
            bool moved = (at - center).sqrMagnitude >= move * move, rescaled = Mathf.Abs(r - radius) > RebuildShare * radius;
            if (!moved && !rescaled && removedNow == removedSeen) return;
            center = at;
            radius = r;
            for (int k = removedSeen; k < removedNow; k++) gone.Add(tables.RemovedOrder[k]);
            removedSeen = removedNow;
            across = field.ChunksAcross;
            var next = ContactWindow.Around(field, Res, at.x, at.y, texel);
            evict.Clear();
            foreach (var kv in chunks)
                if (!next.Holds(kv.Key, across)) evict.Add(kv.Key);
            foreach (int c in evict)
            {
                chunks[c].Dispose();
                chunks.Remove(c);
            }
            missing.Clear();
            for (int kz = next.Az; kz <= next.Bz; kz++)
                for (int kx = next.Ax; kx <= next.Bx; kx++)
                    if (!chunks.ContainsKey(kz * across + kx)) missing.Add(kz * across + kx);
            clock.Restart();
            if (missing.Count == 0)
            {
                convertMs = 0;
                Schedule(next);
                return;
            }
            pending = next;
            converting = StartConvert(field, missing.ToArray(), across);
        }

        static Task<List<(int chunk, ContactChunk table)>> StartConvert(ObjectField field, int[] list, int n) => Task.Run(() =>
        {
            var made = new List<(int chunk, ContactChunk table)>(list.Length);
            foreach (int c in list) made.Add((c, ObjectChunks.Contacts(field, c % n, c / n)));
            return made;
        });

        void Schedule(ContactWindow w)
        {
            window = w;
            splat.Start(new ContactSplatJob { Chunks = chunks, Gone = gone, Data = data, Columns = columnCount, Window = w, Across = across }.Schedule());
        }

        void Upload()
        {
            long ms = clock.ElapsedMilliseconds;
            var sw = Stopwatch.StartNew();
            if (texture == null)
            {
                texture = new Texture2D(Res, Res, TextureFormat.RG16, false, true) { name = "Contact", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                Shader.SetGlobalTexture("_StabbyContact", texture);
            }
            texture.LoadRawTextureData(data);
            texture.Apply(false, false);
            float span = Res * window.TexelM;
            Shader.SetGlobalVector("_StabbyContactUV", new Vector4(1f / span, -window.X0 / span, -window.Z0 / span, 1));
            GameStatus.Lines[World.Name + "~contact"] = $"{World.Name}: ground contact build {++builds}: {columnCount.Value} columns, {span:F0} m window at {window.TexelM:F2} m texels around {window.X0 + 0.5f * span:F0},{window.Z0 + 0.5f * span:F0} in {ms} ms ({chunks.Count} chunk tables, {convertMs} ms converting), upload {sw.Elapsed.TotalMilliseconds:F1} ms";
        }
    }
}
