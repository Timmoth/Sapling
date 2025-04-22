using System.Runtime.InteropServices;

namespace Sapling.Engine
{
    public static unsafe class MemoryHelpers
    {
        public static unsafe T* Allocate<T>(long count) where T : unmanaged
        {
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be positive.");

            const ulong alignment = 64;

            // Use ulong to prevent overflow when calculating size
            ulong elementSize = (ulong)sizeof(T);
            ulong totalSize = elementSize * (ulong)count;

            // Basic safety check: 128 TB max (adjust based on your app domain)
            const ulong maxAllowedSize = 128UL * 1024 * 1024 * 1024 * 1024; // 128 TB
            if (totalSize > maxAllowedSize)
                throw new ArgumentOutOfRangeException(nameof(count), $"Requested size exceeds {maxAllowedSize / (1024 * 1024 * 1024)} GB.");

            void* block = NativeMemory.AlignedAlloc((nuint)totalSize, (nuint)alignment);

            if (block == null)
                throw new OutOfMemoryException($"Failed to allocate {(totalSize / (1024 * 1024))} MB for {typeof(T).Name}.");

            NativeMemory.Clear(block, (nuint)totalSize);

            return (T*)block;
        }

    }
}
