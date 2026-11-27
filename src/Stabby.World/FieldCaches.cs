using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Stabby.World
{
    public sealed class FieldCaches
    {
        public float[] BogPools, QuicksandPools;
        public RoadSegment[] Roads;
        public ObjectInstance[] Sites;
        public int[] SiteChunks;
        public TownPlan[] Plans;
        public LandmarkSketch[] Towns;
        public float[] HotX, HotZ, HotR;
        public byte[] PlacedPack;
        public int[] PlacedAt, PlacedRaw;
        public ulong[] PlacedSum;
    }

    public sealed partial class ObjectField
    {
        public FieldCaches Caches(int every = 1)
        {
            var roads = Roads();
            var hot = Hotspots;
            var c = new FieldCaches
            {
                BogPools = BogPools(),
                QuicksandPools = QuicksandPools(),
                Roads = roads,
                Sites = siteList.ToArray(),
                SiteChunks = siteChunkOf,
                Plans = TownPlans(),
                Towns = Towns.ToArray(),
                HotX = hot.X.ToArray(),
                HotZ = hot.Z.ToArray(),
                HotR = hot.R.ToArray(),
            };
            int count = across * across;
            var packs = new byte[count][];
            var raw = new int[count];
            var sums = new ulong[count];
            Parallel.For(0, count, k =>
            {
                packs[k] = k % every == 0 ? PlacedPack.Pack(Chunk(k % across, k / across), k, out raw[k], out sums[k]) : Array.Empty<byte>();
            });
            var at = new int[count + 1];
            for (int k = 0; k < count; k++) at[k + 1] = at[k] + packs[k].Length;
            var all = new byte[at[count]];
            for (int k = 0; k < count; k++) packs[k].CopyTo(all, at[k]);
            c.PlacedPack = all;
            c.PlacedAt = at;
            c.PlacedRaw = raw;
            c.PlacedSum = sums;
            return c;
        }

        public ObjectField Adopt(FieldCaches c)
        {
            if (c == null) return this;
            towns = new List<LandmarkSketch>(c.Towns);
            var hot = new Geothermal();
            hot.X.AddRange(c.HotX); hot.Z.AddRange(c.HotZ); hot.R.AddRange(c.HotR);
            hotspots = hot;
            float span = WorldChunks.Size * cm;
            var byChunk = new Dictionary<int, List<ObjectInstance>>();
            foreach (var p in c.Plans)
                foreach (var o in p.Members)
                {
                    int k = Math.Min(across - 1, (int)(o.Z / span)) * across + Math.Min(across - 1, (int)(o.X / span));
                    if (!byChunk.TryGetValue(k, out var bin)) byChunk[k] = bin = new List<ObjectInstance>();
                    bin.Add(o);
                }
            townChunks = byChunk;
            townPlans = c.Plans;
            lock (bogLock) bogPools = c.BogPools;
            if (c.QuicksandPools != null) lock (quicksandLock) quicksandPools = c.QuicksandPools;
            lock (roadLock)
            {
                IndexSites(new List<ObjectInstance>(c.Sites), c.SiteChunks);
                IndexRoads(c.Roads);
                roads = c.Roads;
            }
            if (c.PlacedPack != null && c.PlacedAt != null && c.PlacedRaw != null && c.PlacedSum != null && c.PlacedAt.Length == chunks.Length + 1 && c.PlacedRaw.Length == chunks.Length && c.PlacedSum.Length == chunks.Length)
            {
                packRaw = c.PlacedRaw;
                packSum = c.PlacedSum;
                packAt = c.PlacedAt;
                Volatile.Write(ref pack, c.PlacedPack);
            }
            return this;
        }
    }
}
