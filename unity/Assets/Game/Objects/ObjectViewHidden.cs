using Stabby.World;
using Unity.Collections;
using Unity.Entities;

namespace Stabby.Game
{
    public partial class ObjectViewSystem
    {
        struct HideTile
        {
            public long Key;
            public int Def;
        }

        NativeParallelHashSet<ulong> hidden;
        NativeList<HideTile> hideTiles;

        bool Hidden(ulong id) => hidden.IsCreated && hidden.Contains(id);

        void DisposeHidden()
        {
            if (hidden.IsCreated) hidden.Dispose();
            if (hideTiles.IsCreated) hideTiles.Dispose();
        }

        public int HiddenCount => hidden.IsCreated ? hidden.Count() : 0;

        public void Hide(ObjectInstance o)
        {
            if (!hidden.IsCreated) hidden = new NativeParallelHashSet<ulong>(64, Allocator.Persistent);
            if (!hidden.Add(o.Id) || tile == null || o.Def >= tile.Length) return;
            if (!hideTiles.IsCreated) hideTiles = new NativeList<HideTile>(8, Allocator.Persistent);
            hideTiles.Add(new HideTile { Key = Key(o.Def, (int)(o.X / tile[o.Def]), (int)(o.Z / tile[o.Def])), Def = o.Def });
        }

        void DespawnHidden()
        {
            if (!hideTiles.IsCreated || hideTiles.Length == 0) return;
            var doomed = new NativeList<Entity>(Allocator.Temp);
            foreach (var h in hideTiles)
            {
                if (!spawned.Contains(h.Key)) continue;
                int before = doomed.Length;
                if (tileEntities.TryGetFirstValue(h.Key, out var e, out var it))
                    do doomed.Add(e); while (tileEntities.TryGetNextValue(out e, ref it));
                tileEntities.Remove(h.Key);
                spawned.Remove(h.Key);
                liveByDef[h.Def] -= doomed.Length - before;
                liveEntities -= doomed.Length - before;
            }
            hideTiles.Clear();
            if (doomed.Length > 0) EntityManager.DestroyEntity(doomed.AsArray());
            doomed.Dispose();
        }
    }
}
