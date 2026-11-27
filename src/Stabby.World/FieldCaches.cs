using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Stabby.World
{
    public sealed class FieldCaches
    {
        public float[] BogPools;
        public RoadSegment[] Roads;
        public ObjectInstance[] Sites;
        public int[] SiteChunks;
        public TownPlan[] Plans;
        public LandmarkSketch[] Towns;
        public float[] HotX, HotZ, HotR;
        public ObjectInstance[] Placed;
        public int[] PlacedStart;
    }

    public sealed partial class ObjectField
    {
        public FieldCaches Caches()
        {
            var roads = Roads();
            var hot = Hotspots;
            var c = new FieldCaches
            {
                BogPools = BogPools(),
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
            var placed = new ObjectInstance[count][];
            Parallel.For(0, count, k => placed[k] = Chunk(k % across, k / across));
            var start = new int[count + 1];
            for (int k = 0; k < count; k++) start[k + 1] = start[k] + placed[k].Length;
            var all = new ObjectInstance[start[count]];
            for (int k = 0; k < count; k++) placed[k].CopyTo(all, start[k]);
            c.Placed = all;
            c.PlacedStart = start;
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
            lock (roadLock)
            {
                IndexSites(new List<ObjectInstance>(c.Sites), c.SiteChunks);
                IndexRoads(c.Roads);
                roads = c.Roads;
            }
            if (c.Placed != null && c.PlacedStart != null && c.PlacedStart.Length == chunks.Length + 1)
                for (int k = 0; k < chunks.Length; k++)
                    Volatile.Write(ref chunks[k], c.Placed.AsSpan(c.PlacedStart[k], c.PlacedStart[k + 1] - c.PlacedStart[k]).ToArray());
            return this;
        }
    }
}
