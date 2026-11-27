using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Stabby.World
{
    public sealed class DetailArrays
    {
        public float[] Amp, Offsets, Bands;
        public float Period;
        public RiverPiece[] Pieces;
        public int[] Start, Items;
    }

    public sealed class GroundBake
    {
        public const int Format = 2, Sections = 5;

        public ulong Key;
        public DetailArrays Detail;
        public FeatureEdit[] Edits;
        public CaveGraph[] Caves;
        public ObjectInstance[] Discoveries;
        public RoadSegment[] Roads;
        public CaveBrook[] Brooks;
        public FieldCaches Field;

        public static GroundBake Run(GeneratedWorld w, ulong key, bool caches, out TerrainDetail detail) => Run(w, key, caches, out detail, out _);

        public static GroundBake Run(GeneratedWorld w, ulong key, bool caches, out TerrainDetail detail, out ObjectField live)
        {
            detail = new TerrainDetail(w);
            var edits = Features.Place(w, detail, out var caves, out var discoveries, out var roads, out live);
            var bake = new GroundBake
            {
                Key = key, Detail = detail.Arrays, Edits = edits, Caves = caves, Discoveries = discoveries, Roads = roads,
                Brooks = CaveBrooks.Build(w, detail, edits, caves),
            };
            if (caches && w.Objects != null && w.Biome != null) bake.Field = new ObjectField(w, detail, edits, w.Objects, discoveries).Caches();
            return bake;
        }

        public static ulong KeyOf(IReadOnlyList<byte[]> files, float density)
        {
            var all = new byte[files.Count + 1][];
            for (int i = 0; i < files.Count; i++) all[i] = files[i];
            all[files.Count] = Encoding.ASCII.GetBytes($"ground {Format} generator {WorldConfig.GeneratorVersion} chunks {WorldChunks.Version} density {BitConverter.SingleToInt32Bits(density)}");
            return WorldFile.ContentHash(all);
        }

        public byte[] Write()
        {
            var parts = new byte[Sections][];
            Parallel.For(0, Sections, s =>
            {
                var o = new Out(new MemoryStream());
                switch (s)
                {
                    case 0: o.Raw(Detail.Amp); break;
                    case 1: o.Raw(Detail.Offsets); break;
                    case 2: o.Raw(Detail.Start); o.Raw(Detail.Items); o.Raw(Detail.Bands); o.Float(Detail.Period); o.Raw(Detail.Pieces); break;
                    case 3: o.Raw(Field?.Placed); o.Raw(Field?.PlacedStart); break;
                    default: Rest(o); break;
                }
                parts[s] = o.ToArray();
            });
            var packed = new byte[Sections][];
            Parallel.For(0, Sections, s =>
            {
                var m = new MemoryStream();
                using (var z = new DeflateStream(m, CompressionLevel.Optimal, true)) z.Write(parts[s], 0, parts[s].Length);
                packed[s] = m.ToArray();
            });
            var head = new Out(new MemoryStream());
            head.Int(Format); head.Long(Key); head.Int(Sections);
            for (int s = 0; s < Sections; s++) { head.Int(parts[s].Length); head.Int(packed[s].Length); head.Long(WorldChunks.Checksum(parts[s])); }
            var m2 = new MemoryStream();
            var h = head.ToArray();
            m2.Write(h, 0, h.Length);
            foreach (var p in packed) m2.Write(p, 0, p.Length);
            return m2.ToArray();
        }

        public static GroundBake Read(byte[] data, int at, int count)
        {
            var head = new In(data, at, count);
            if (head.Int() != Format) throw new FormatException("ground bake has another format");
            ulong key = head.Long();
            if (head.Int() != Sections) throw new FormatException("ground bake has another section count");
            var raw = new int[Sections];
            var len = new int[Sections];
            var sum = new ulong[Sections];
            var start = new int[Sections];
            long o = 0;
            for (int s = 0; s < Sections; s++)
            {
                raw[s] = head.Int(); len[s] = head.Int(); sum[s] = head.Long();
                if (raw[s] < 0 || len[s] < 0) throw new FormatException($"ground bake section {s} has a negative length");
                o += len[s];
            }
            if (o != head.Left) throw new FormatException($"ground bake sections need {o} bytes, {head.Left} remain");
            int pos = head.Position;
            for (int s = 0; s < Sections; s++) { start[s] = pos; pos += len[s]; }
            var parts = new byte[Sections][];
            try
            {
                Parallel.For(0, Sections, s =>
                {
                    var b = new byte[raw[s]];
                    int got = 0;
                    using (var z = new DeflateStream(new MemoryStream(data, start[s], len[s], false), CompressionMode.Decompress))
                    {
                        for (int r; got < b.Length && (r = z.Read(b, got, b.Length - got)) > 0;) got += r;
                        if (got < b.Length || z.ReadByte() >= 0) throw new FormatException($"ground bake section {s} inflates to the wrong length");
                    }
                    if (WorldChunks.Checksum(b) != sum[s]) throw new FormatException($"ground bake section {s} fails its checksum");
                    parts[s] = b;
                });
            }
            catch (AggregateException e)
            {
                var f = e.Flatten().InnerExceptions[0];
                throw f is FormatException ? (FormatException)f : new FormatException($"ground bake is corrupt: {f.Message}", f);
            }
            var bake = new GroundBake { Key = key, Detail = new DetailArrays() };
            var d = bake.Detail;
            var i0 = new In(parts[0]); d.Amp = i0.Raw<float>(); i0.End();
            var i1 = new In(parts[1]); d.Offsets = i1.Raw<float>(); i1.End();
            var i2 = new In(parts[2]); d.Start = i2.Raw<int>(); d.Items = i2.Raw<int>(); d.Bands = i2.Raw<float>(); d.Period = i2.Float(); d.Pieces = i2.Raw<RiverPiece>(); i2.End();
            var i3 = new In(parts[3]); var placed = i3.Raw<ObjectInstance>(); var placedStart = i3.Raw<int>(); i3.End();
            var i4 = new In(parts[4]); bake.Rest(i4); i4.End();
            if ((placed == null) != (placedStart == null) || placedStart != null && (bake.Field == null || !Ascending(placedStart, placed.Length)))
                throw new FormatException("ground bake placed objects do not fit their chunk table");
            if (bake.Field != null) { bake.Field.Placed = placed; bake.Field.PlacedStart = placedStart; }
            if (d.Amp == null || d.Offsets == null || d.Start == null || d.Items == null || d.Bands == null || d.Pieces == null || bake.Edits == null || bake.Caves == null || bake.Roads == null || bake.Brooks == null)
                throw new FormatException("ground bake is missing a section");
            return bake;
        }

        void Rest(Out o)
        {
            o.Int(Edits.Length);
            foreach (var e in Edits)
            {
                o.Int(e.Kind); o.Int(e.Shape);
                o.Float(e.Ax); o.Float(e.Ay); o.Float(e.Az); o.Float(e.Bx); o.Float(e.By); o.Float(e.Bz); o.Float(e.R);
                o.Float(e.W); o.Float(e.H); o.Float(e.P); o.Float(e.T); o.Float(e.D); o.Float(e.F); o.Float(e.N); o.Float(e.S); o.Float(e.Coarse);
                o.Bool(e.Fine); o.Bool(e.Ice); o.Bool(e.Obsidian); o.Bool(e.Link);
            }
            o.Int(Caves.Length);
            foreach (var g in Caves)
            {
                o.Str(g.Class); o.Int(g.Tile); o.Bool(g.Crystal); o.Bool(g.Ice); o.Bool(g.Obsidian);
                o.Int(g.Nodes.Count);
                foreach (var n in g.Nodes) { o.Float(n.X); o.Float(n.Y); o.Float(n.Z); o.Float(n.Hx); o.Float(n.Hz); o.Bool(n.Entrance); o.Bool(n.Hall); }
                o.Int(g.Edges.Count);
                foreach (var e in g.Edges) { o.Int(e.A); o.Int(e.B); o.Float(e.R); o.Raw(e.Points); }
            }
            o.Raw(Discoveries);
            o.Raw(Roads);
            o.Int(Brooks.Length);
            foreach (var b in Brooks)
            {
                o.Int(b.Graph); o.Int(b.End); o.Int(b.Outlet); o.Int(b.Fall);
                o.Float(b.Flow); o.Float(b.BasinX); o.Float(b.BasinZ); o.Float(b.BasinReach); o.Float(b.PoolR);
                o.Raw(b.Points); o.Raw(b.Tops); o.Raw(b.Nodes);
            }
            var f = Field;
            o.Bool(f != null);
            if (f == null) return;
            o.Raw(f.BogPools); o.Raw(f.Roads); o.Raw(f.Sites); o.Raw(f.SiteChunks);
            o.Int(f.Plans.Length);
            foreach (var p in f.Plans)
            {
                o.Int(p.Town); o.Int(p.Landmark);
                o.Float(p.X); o.Float(p.Z); o.Float(p.Ux); o.Float(p.Uz); o.Float(p.Radius); o.Float(p.PlazaM); o.Float(p.LaneM); o.Float(p.LaneWidthM);
                o.Raw(p.Members.ToArray());
            }
            o.Int(f.Towns.Length);
            foreach (var t in f.Towns) { o.Str(t.Kind); o.Float(t.X); o.Float(t.Y); }
            o.Raw(f.HotX); o.Raw(f.HotZ); o.Raw(f.HotR);
        }

        void Rest(In i)
        {
            Edits = new FeatureEdit[i.Count(8)];
            for (int k = 0; k < Edits.Length; k++)
                Edits[k] = new FeatureEdit
                {
                    Kind = i.Int(), Shape = i.Int(),
                    Ax = i.Float(), Ay = i.Float(), Az = i.Float(), Bx = i.Float(), By = i.Float(), Bz = i.Float(), R = i.Float(),
                    W = i.Float(), H = i.Float(), P = i.Float(), T = i.Float(), D = i.Float(), F = i.Float(), N = i.Float(), S = i.Float(), Coarse = i.Float(),
                    Fine = i.Bool(), Ice = i.Bool(), Obsidian = i.Bool(), Link = i.Bool(),
                };
            Caves = new CaveGraph[i.Count(4)];
            for (int k = 0; k < Caves.Length; k++)
            {
                var g = new CaveGraph { Class = i.Str(), Tile = i.Int(), Crystal = i.Bool(), Ice = i.Bool(), Obsidian = i.Bool() };
                for (int n = i.Count(22); n > 0; n--) g.Nodes.Add(new CaveNode { X = i.Float(), Y = i.Float(), Z = i.Float(), Hx = i.Float(), Hz = i.Float(), Entrance = i.Bool(), Hall = i.Bool() });
                for (int n = i.Count(16); n > 0; n--) g.Edges.Add(new CaveEdge { A = i.Int(), B = i.Int(), R = i.Float(), Points = i.Raw<float>() });
                Caves[k] = g;
            }
            Discoveries = i.Raw<ObjectInstance>();
            Roads = i.Raw<RoadSegment>();
            Brooks = new CaveBrook[i.Count(48)];
            for (int k = 0; k < Brooks.Length; k++)
                Brooks[k] = new CaveBrook
                {
                    Graph = i.Int(), End = i.Int(), Outlet = i.Int(), Fall = i.Int(),
                    Flow = i.Float(), BasinX = i.Float(), BasinZ = i.Float(), BasinReach = i.Float(), PoolR = i.Float(),
                    Points = i.Raw<float>(), Tops = i.Raw<float>(), Nodes = i.Raw<int>(),
                };
            if (!i.Bool()) return;
            var f = Field = new FieldCaches { BogPools = i.Raw<float>(), Roads = i.Raw<RoadSegment>(), Sites = i.Raw<ObjectInstance>(), SiteChunks = i.Raw<int>() };
            f.Plans = new TownPlan[i.Count(44)];
            for (int k = 0; k < f.Plans.Length; k++)
            {
                var p = new TownPlan
                {
                    Town = i.Int(), Landmark = i.Int(),
                    X = i.Float(), Z = i.Float(), Ux = i.Float(), Uz = i.Float(), Radius = i.Float(), PlazaM = i.Float(), LaneM = i.Float(), LaneWidthM = i.Float(),
                };
                p.Members.AddRange(i.Raw<ObjectInstance>() ?? throw new FormatException("ground bake town plan has no members"));
                f.Plans[k] = p;
            }
            f.Towns = new LandmarkSketch[i.Count(12)];
            for (int k = 0; k < f.Towns.Length; k++) f.Towns[k] = new LandmarkSketch { Kind = i.Str(), X = i.Float(), Y = i.Float() };
            f.HotX = i.Raw<float>(); f.HotZ = i.Raw<float>(); f.HotR = i.Raw<float>();
            if (f.BogPools == null || f.Roads == null || f.Sites == null || f.SiteChunks == null || f.Sites.Length != f.SiteChunks.Length || f.HotX == null || f.HotZ == null || f.HotR == null || f.HotX.Length != f.HotZ.Length || f.HotX.Length != f.HotR.Length)
                throw new FormatException("ground bake object caches do not fit together");
        }

        static bool Ascending(int[] start, int total)
        {
            if (start.Length == 0 || start[0] != 0 || start[start.Length - 1] != total) return false;
            for (int k = 1; k < start.Length; k++) if (start[k] < start[k - 1]) return false;
            return true;
        }

        static class Size<T> where T : unmanaged
        {
            public static readonly int Bytes = MemoryMarshal.AsBytes(new T[1].AsSpan()).Length;
        }

        sealed class Out
        {
            readonly MemoryStream m;
            readonly byte[] b = new byte[8];

            public Out(MemoryStream into) => m = into;

            public void Int(int v) { BitConverter.TryWriteBytes(b, v); m.Write(b, 0, 4); }
            public void Long(ulong v) { BitConverter.TryWriteBytes(b, v); m.Write(b, 0, 8); }
            public void Float(float v) => Int(BitConverter.SingleToInt32Bits(v));
            public void Bool(bool v) => m.WriteByte(v ? (byte)1 : (byte)0);

            public void Str(string s)
            {
                if (s == null) { Int(-1); return; }
                var t = Encoding.UTF8.GetBytes(s);
                Int(t.Length);
                m.Write(t, 0, t.Length);
            }

            public void Raw<T>(T[] a) where T : unmanaged
            {
                if (a == null) { Int(-1); return; }
                Int(a.Length);
                m.Write(MemoryMarshal.AsBytes(a.AsSpan()));
            }

            public byte[] ToArray() => m.ToArray();
        }

        sealed class In
        {
            readonly byte[] d;
            readonly int end;
            int o;

            public In(byte[] d) : this(d, 0, d.Length) { }

            public In(byte[] d, int at, int count)
            {
                if (at < 0 || count < 0 || at > d.Length - count) throw new FormatException("ground bake lies outside the file");
                this.d = d; o = at; end = at + count;
            }

            public int Position => o;
            public int Left => end - o;

            void Need(long n) { if (n < 0 || n > end - o) throw new FormatException("ground bake ends early"); }

            public int Int() { Need(4); int v = BitConverter.ToInt32(d, o); o += 4; return v; }
            public ulong Long() { Need(8); ulong v = BitConverter.ToUInt64(d, o); o += 8; return v; }
            public float Float() => BitConverter.Int32BitsToSingle(Int());
            public bool Bool() { Need(1); return d[o++] != 0; }

            public int Count(int minBytes)
            {
                int n = Int();
                Need((long)n * minBytes);
                return n;
            }

            public string Str()
            {
                int n = Int();
                if (n == -1) return null;
                Need(n);
                var s = Encoding.UTF8.GetString(d, o, n);
                o += n;
                return s;
            }

            public T[] Raw<T>() where T : unmanaged
            {
                int n = Int();
                if (n == -1) return null;
                long bytes = (long)n * Size<T>.Bytes;
                Need(bytes);
                var a = new T[n];
                new ReadOnlySpan<byte>(d, o, (int)bytes).CopyTo(MemoryMarshal.AsBytes(a.AsSpan()));
                o += (int)bytes;
                return a;
            }

            public void End() { if (o != end) throw new FormatException($"ground bake has {end - o} stray bytes"); }
        }
    }
}
