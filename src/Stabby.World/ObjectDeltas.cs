using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public enum RemoveResult : byte { Ok, NoChunk, NoObject, NotInteractive, AlreadyRemoved, OutOfReach }

    public sealed class ObjectDeltas
    {
        readonly HashSet<ulong> removed = new HashSet<ulong>();
        readonly SortedDictionary<int, List<ulong>> byChunk = new SortedDictionary<int, List<ulong>>();

        public int Count => removed.Count;
        public bool Removed(ulong id) => removed.Contains(id);
        public IEnumerable<int> Chunks => byChunk.Keys;

        public IReadOnlyList<ulong> InChunk(int chunk) => byChunk.TryGetValue(chunk, out var list) ? list : (IReadOnlyList<ulong>)Array.Empty<ulong>();

        public bool Apply(ulong id)
        {
            if (!removed.Add(id)) return false;
            int c = ObjectIds.Chunk(id);
            if (!byChunk.TryGetValue(c, out var list)) byChunk[c] = list = new List<ulong>();
            list.Add(id);
            return true;
        }

        public RemoveResult Check(ObjectField field, ulong id, float px, float py, float pz, out ObjectInstance found)
        {
            found = default;
            int chunk = ObjectIds.Chunk(id), def = ObjectIds.Def(id), across = field.ChunksAcross;
            if (chunk >= across * across) return RemoveResult.NoChunk;
            if (def >= field.Library.Objects.Count) return RemoveResult.NoObject;
            if (!field.Library.IsInteractive(def)) return RemoveResult.NotInteractive;
            if (!TryFind(field, id, out found)) return RemoveResult.NoObject;
            if (removed.Contains(id)) return RemoveResult.AlreadyRemoved;
            float dx = found.X - px, dy = found.Y - py, dz = found.Z - pz;
            float reach = field.Library.Objects[def].ReachM + field.Footprint(def) * found.Size;
            return dx * dx + dy * dy + dz * dz <= reach * reach ? RemoveResult.Ok : RemoveResult.OutOfReach;
        }

        public RemoveResult TryRemove(ObjectField field, ulong id, float px, float py, float pz, out ObjectInstance found)
        {
            var r = Check(field, id, px, py, pz, out found);
            if (r == RemoveResult.Ok) Apply(id);
            return r;
        }

        public static bool TryFind(ObjectField field, ulong id, out ObjectInstance found)
        {
            int chunk = ObjectIds.Chunk(id), across = field.ChunksAcross;
            found = default;
            if (chunk >= across * across) return false;
            foreach (var o in field.Chunk(chunk % across, chunk / across))
                if (o.Id == id) { found = o; return true; }
            return false;
        }

        public static void InReach(ObjectField field, float px, float py, float pz, List<ulong> ids)
        {
            ids.Clear();
            var dist = new List<float>();
            float cm = field.ChunkMeters;
            int across = field.ChunksAcross;
            int cx0 = Math.Max(0, (int)Math.Floor(px / cm) - 1), cx1 = Math.Min(across - 1, (int)Math.Floor(px / cm) + 1);
            int cz0 = Math.Max(0, (int)Math.Floor(pz / cm) - 1), cz1 = Math.Min(across - 1, (int)Math.Floor(pz / cm) + 1);
            for (int cz = cz0; cz <= cz1; cz++)
                for (int cx = cx0; cx <= cx1; cx++)
                    foreach (var o in field.Chunk(cx, cz))
                    {
                        if (!field.Library.IsInteractive(o.Def)) continue;
                        float dx = o.X - px, dy = o.Y - py, dz = o.Z - pz, d = dx * dx + dy * dy + dz * dz;
                        float reach = field.Library.Objects[o.Def].ReachM + field.Footprint(o.Def) * o.Size;
                        if (d > reach * reach) continue;
                        int at = dist.Count;
                        while (at > 0 && dist[at - 1] > d) at--;
                        dist.Insert(at, d);
                        ids.Insert(at, o.Id);
                    }
        }

        public bool Nearest(ObjectField field, float px, float py, float pz, out ulong best)
        {
            var ids = new List<ulong>();
            InReach(field, px, py, pz, ids);
            foreach (ulong id in ids)
                if (!removed.Contains(id)) { best = id; return true; }
            best = 0;
            return false;
        }
    }
}
