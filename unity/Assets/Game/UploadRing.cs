using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Stabby.Game
{
    public sealed class UploadRing<T> where T : struct
    {
        const int Slots = 3;
        readonly GraphicsBuffer[] slots = new GraphicsBuffer[Slots];
        readonly GraphicsBuffer.Target target;
        readonly int stride;
        readonly string name;
        int next;
        public GraphicsBuffer Current { get; private set; }
        public bool Locked { get; private set; }

        public long Bytes
        {
            get
            {
                long total = 0;
                foreach (var b in slots)
                    if (b != null && b.IsValid()) total += (long)b.count * b.stride;
                return total;
            }
        }

        public UploadRing(GraphicsBuffer.Target target, int stride, string name)
        {
            this.target = target;
            this.stride = stride;
            this.name = name;
        }

        public NativeArray<T> Lock(int count)
        {
            count = math.max(count, 1);
            var b = slots[next];
            if (b == null || b.count < count || PoolSize.Oversized(b.count, count))
            {
                int old = b == null ? 0 : b.count;
                b?.Release();
                int size = old < count ? math.max(count, old == 0 ? 64 : old + old / 2) : PoolSize.Trimmed(count);
                b = slots[next] = new GraphicsBuffer(target, GraphicsBuffer.UsageFlags.LockBufferForWrite, size, stride) { name = name };
            }
            Locked = true;
            return b.LockBufferForWrite<T>(0, count);
        }

        public void Unlock(int written)
        {
            if (!Locked) return;
            Locked = false;
            Current = slots[next];
            Current.UnlockBufferAfterWrite<T>(written);
            next = (next + 1) % Slots;
        }

        public void Release()
        {
            if (Locked) slots[next].UnlockBufferAfterWrite<T>(0);
            Locked = false;
            for (int i = 0; i < Slots; i++)
            {
                slots[i]?.Release();
                slots[i] = null;
            }
            Current = null;
        }
    }
}
