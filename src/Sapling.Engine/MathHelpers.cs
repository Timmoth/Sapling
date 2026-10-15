using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sapling.Engine
{
    public static unsafe class MemoryHelpers
    {
        // Test-only fault injection and balance tracking. These are null in production use;
        // the null check sits on construction-time allocation paths, never node hot paths.
        // FaultInjector: called with the element count before allocating; return true to
        // simulate allocation failure without touching the native heap.
        internal static Func<long, bool>? FaultInjector;
        internal static Action<IntPtr>? AllocationObserved;
        internal static Action<IntPtr>? FreeObserved;

        internal static void InjectFaultForTesting(Func<long, bool>? fault, Action<IntPtr>? allocObserved, Action<IntPtr>? freeObserved)
        {
            FaultInjector = fault;
            AllocationObserved = allocObserved;
            FreeObserved = freeObserved;
        }

        public static void FreeAligned(void* block)
        {
            if (block == null)
                return;
            var hook = FreeObserved;
            if (hook != null)
                hook((IntPtr)block);
            NativeMemory.AlignedFree(block);
        }

        public static unsafe T* Allocate<T>(long count) where T : unmanaged
        {
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be positive.");

            const ulong alignment = 64;

            // Use ulong to prevent overflow when calculating size
            ulong elementSize = (ulong)sizeof(T);
            ulong countU = (ulong)count;

            // Basic safety check: 128 TB max (adjust based on your app domain)
            const ulong maxAllowedSize = 128UL * 1024 * 1024 * 1024 * 1024; // 128 TB
            if (elementSize != 0 && countU > maxAllowedSize / elementSize)
                throw new ArgumentOutOfRangeException(nameof(count), $"Requested size exceeds {maxAllowedSize / (1024 * 1024 * 1024)} GB.");

            ulong totalSize = elementSize * countU;

            // Reject sizes that do not fit the address width (matters on 32-bit targets such as wasm32)
            if (totalSize > nuint.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(count), "Requested size exceeds the addressable pointer width.");

            var fault = FaultInjector;
            if (fault != null && fault(count))
                throw new OutOfMemoryException($"Injected allocation fault for {typeof(T).Name}[{count}].");

            void* block = NativeMemory.AlignedAlloc((nuint)totalSize, (nuint)alignment);

            if (block == null)
                throw new OutOfMemoryException($"Failed to allocate {(totalSize / (1024 * 1024))} MB for {typeof(T).Name}.");

            NativeMemory.Clear(block, (nuint)totalSize);

            var observe = AllocationObserved;
            if (observe != null)
                observe((IntPtr)block);

            return (T*)block;
        }

        public static unsafe T* AllocateMultiThreaded<T>(long count) where T : unmanaged
        {
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be positive.");

            const ulong alignment = 64;
            ulong elementSize = (ulong)sizeof(T);
            ulong countU = (ulong)count;

            const ulong maxAllowedSize = 128UL * 1024 * 1024 * 1024 * 1024; // 128 TB
            if (elementSize != 0 && countU > maxAllowedSize / elementSize)
                throw new ArgumentOutOfRangeException(nameof(count), $"Requested size exceeds {maxAllowedSize / (1024 * 1024 * 1024)} GB.");

            ulong totalSize = elementSize * countU;
            if (totalSize > nuint.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(count), "Requested size exceeds the addressable pointer width.");

            var fault = FaultInjector;
            if (fault != null && fault(count))
                throw new OutOfMemoryException($"Injected allocation fault for {typeof(T).Name}[{count}].");

            void* block = NativeMemory.AlignedAlloc((nuint)totalSize, (nuint)alignment);
            if (block == null)
                throw new OutOfMemoryException($"Failed to allocate {(totalSize / (1024 * 1024))} MB for {typeof(T).Name}.");

            var observe = AllocationObserved;
            if (observe != null)
                observe((IntPtr)block);

            // Parallel zero-init. Runtimes without blocking waits (browser-wasm) must
            // zero the block inline; Task.WaitAll throws "Cannot wait on monitors" there.
            int threadCount = Environment.ProcessorCount;
            if (threadCount <= 1 || OperatingSystem.IsBrowser())
            {
                ZeroBytes((byte*)block, totalSize);
                return (T*)block;
            }

            try
            {
                // Parallel.For joins all scheduled work before propagating a failure, so the
                // allocation can be released even if scheduling or a worker fails.
                long chunkSize = count / threadCount;
                Parallel.For(0, threadCount, i =>
                {
                    long start = i * chunkSize;
                    long length = i == threadCount - 1 ? count - start : chunkSize;
                    if (length > 0)
                        ZeroBytes((byte*)block + start * sizeof(T), (ulong)length * (ulong)sizeof(T));
                });
            }
            catch
            {
                FreeAligned(block);
                throw;
            }

            return (T*)block;
        }

        private static unsafe void ZeroBytes(byte* start, ulong bytes)
        {
            const uint maxChunk = uint.MaxValue;
            while (bytes > 0)
            {
                uint take = bytes > maxChunk ? maxChunk : (uint)bytes;
                Unsafe.InitBlockUnaligned(start, 0, take);
                start += take;
                bytes -= take;
            }
        }

    }
}
