using System;

namespace Stabby.World
{
    public sealed class HotspotSketch
    {
        public const int Arc = 0, Plume = 1, Ocean = 2;
        public int Kind;
        public float X, Y, Radius;
    }

    public sealed partial class WorldSketch
    {
        public const float OceanChance = 0.6f, OceanRadiusMin = 0.065f, OceanRadiusMax = 0.085f, OceanEdge = 0.14f, OceanClear = 1.9f, OceanIsletClear = 1.2f;
        public const float ArcChance = 0.5f, ArcInlandM = 2000f, PlumeInlandM = 1500f, HotspotGapM = 5000f, ArcLandmarkM = 2500f;
        public const int OceanTries = 120, PlumesMax = 2, PlumeTries = 60;

        void AddOceanHotspot(WorldConfig c)
        {
            int n = c.Size;
            var rng = Rng.Stream(c.Seed, "hotspots-ocean");
            if (rng.NextFloat() >= OceanChance) return;
            for (int t = 0; t < OceanTries; t++)
            {
                float x = n * rng.Range(OceanEdge, 1f - OceanEdge), y = n * rng.Range(OceanEdge, 1f - OceanEdge), r = n * rng.Range(OceanRadiusMin, OceanRadiusMax);
                bool ok = true;
                foreach (var m in Landmasses)
                    if (m.Gap(x, y, CoastReach) - WarpReach * n < OceanClear * r + 3f) ok = false;
                foreach (var o in Islets)
                    if (o.Gap(x, y, CoastReach) < OceanIsletClear * r + 3f) ok = false;
                if (!ok) continue;
                Islets.Add(new LandmassSketch { X = x, Y = y, Radius = r });
                Hotspots.Add(new HotspotSketch { Kind = HotspotSketch.Ocean, X = x, Y = y, Radius = r });
                return;
            }
        }

        public bool Volcanic(LandmassSketch m)
        {
            foreach (var h in Hotspots)
                if (h.Kind == HotspotSketch.Ocean && h.X == m.X && h.Y == m.Y) return true;
            return false;
        }

        void AddHotspots(WorldConfig c, float[] inland)
        {
            int n = c.Size;
            float cm = c.CellMeters;
            var rng = Rng.Stream(c.Seed, "hotspots");
            foreach (var r in Ranges)
            {
                if (rng.NextFloat() >= ArcChance || r.X.Length == 0) continue;
                int start = rng.Range(0, r.X.Length), k = -1;
                for (int j = 0; j < r.X.Length && k < 0; j++)
                {
                    int q = (start + j) % r.X.Length;
                    int i = Math.Min(n - 1, (int)r.Y[q]) * n + Math.Min(n - 1, (int)r.X[q]);
                    if (Land[i] && inland[i] * cm >= ArcInlandM && Apart(r.X[q], r.Y[q], cm) && Clear(r.X[q], r.Y[q], cm)) k = q;
                }
                if (k < 0) continue;
                Hotspots.Add(new HotspotSketch { Kind = HotspotSketch.Arc, X = r.X[k], Y = r.Y[k] });
            }
            int plumes = rng.Range(0, PlumesMax + 1);
            for (int t = 0; t < PlumeTries && plumes > 0; t++)
            {
                float x = n * rng.Range(0.1f, 0.9f), y = n * rng.Range(0.1f, 0.9f);
                int i = (int)y * n + (int)x;
                if (!Land[i] || inland[i] * cm < PlumeInlandM || !Apart(x, y, cm)) continue;
                Hotspots.Add(new HotspotSketch { Kind = HotspotSketch.Plume, X = x, Y = y });
                plumes--;
            }
        }

        bool Apart(float x, float y, float cm)
        {
            foreach (var h in Hotspots)
                if ((h.X - x) * (h.X - x) + (h.Y - y) * (h.Y - y) < HotspotGapM * HotspotGapM / (cm * cm)) return false;
            return true;
        }

        bool Clear(float x, float y, float cm)
        {
            foreach (var l in Landmarks)
                if ((l.X - x) * (l.X - x) + (l.Y - y) * (l.Y - y) < ArcLandmarkM * ArcLandmarkM / (cm * cm)) return false;
            return true;
        }
    }
}
