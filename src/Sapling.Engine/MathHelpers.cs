using System.Runtime.InteropServices;

namespace Sapling.Engine
{
    public static unsafe class MemoryHelpers
    {
        public static T* Allocate<T>(int count) where T : unmanaged
        {
            const nuint alignment = 64;

            // Use ulong to avoid overflow when calculating size
            ulong totalSize = (ulong)sizeof(T) * (ulong)count;

            // Check if allocation would exceed nuint.MaxValue
            if (totalSize > nuint.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(count), "Requested allocation is too large");

            nuint size = (nuint)totalSize;

            void* block = NativeMemory.AlignedAlloc(size, alignment);
            if (block == null)
                throw new OutOfMemoryException($"Failed to allocate {size / (1024 * 1024)} MB for {typeof(T).Name}");

            NativeMemory.Clear(block, size);
            return (T*)block;
        }
    }
}
