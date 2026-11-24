using Stabby.World;
using Unity.Collections;
using Unity.Entities;

namespace Stabby.Game
{
    public partial class ObjectViewSystem
    {
        NativeParallelHashSet<ulong> hidden;

        bool Hidden(ulong id) => hidden.IsCreated && hidden.Contains(id);

        void DisposeHidden()
        {
            if (hidden.IsCreated) hidden.Dispose();
        }

        public int HiddenCount => hidden.IsCreated ? hidden.Count() : 0;

        public void Hide(ObjectInstance o)
        {
            scan.Complete();
            if (!hidden.IsCreated) hidden = new NativeParallelHashSet<ulong>(64, Allocator.Persistent);
            if (!hidden.Add(o.Id) || tile == null || o.Def >= tile.Length) return;
            long key = Key(o.Def, (int)(o.X / tile[o.Def]), (int)(o.Z / tile[o.Def]));
            if (!spawned.Contains(key)) return;
            var doomed = new NativeList<Entity>(Allocator.Temp);
            if (tileEntities.TryGetFirstValue(key, out var e, out var it))
                do doomed.Add(e); while (tileEntities.TryGetNextValue(out e, ref it));
            tileEntities.Remove(key);
            spawned.Remove(key);
            liveByDef[o.Def] -= doomed.Length;
            liveEntities -= doomed.Length;
            if (doomed.Length > 0) EntityManager.DestroyEntity(doomed.AsArray());
            doomed.Dispose();
        }
    }
}
