using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics;

namespace Sapling.Engine.Evaluation;
#if AVX512
using AvxIntrinsics = System.Runtime.Intrinsics.X86.Avx512BW;
using VectorType = System.Runtime.Intrinsics.Vector512;
using VectorInt = System.Runtime.Intrinsics.Vector512<int>;
using VectorShort = System.Runtime.Intrinsics.Vector512<short>;
#else
using AvxIntrinsics = Avx2;
using VectorType = Vector256;
using VectorInt = Vector256<int>;
using VectorShort = Vector256<short>;
#endif
public static class NnueWeights
{
    public const int InputSize = 768;
    public const int Layer1Size = 768;

    public const short OutputBuckets = 8;

    public static readonly unsafe VectorShort* FeatureWeights;
    public static readonly unsafe VectorShort* FeatureBiases;
    public static readonly unsafe VectorShort* OutputWeights;
    public static readonly short[] OutputBiases = new short[OutputBuckets];

    static unsafe NnueWeights()
    {
        var assembly = Assembly.GetAssembly(typeof(GameState));
        var info = assembly.GetName();
        var name = info.Name;
        using var stream = assembly
            .GetManifestResourceStream($"{name}.Resources.sapling.nnue")!;

        var featureWeights = new short[InputSize * Layer1Size];
        var featureBiases = new short[Layer1Size];
        var outputWeights = new short[Layer1Size * 2 * OutputBuckets];

        using var reader = new BinaryReader(stream, Encoding.UTF8, false);
        for (var i = 0; i < featureWeights.Length; i++)
        {
            featureWeights[i] = reader.ReadInt16();
        }

        for (var i = 0; i < featureBiases.Length; i++)
        {
            featureBiases[i] = reader.ReadInt16();
        }

        for (var i = 0; i < outputWeights.Length; i++)
        {
            outputWeights[i] = reader.ReadInt16();
        }
        var transposedWeights = new short[outputWeights.Length];

        // Transposing logic
        for (int i = 0; i < 2 * Layer1Size; i++)
        {
            for (int j = 0; j < OutputBuckets; j++)
            {
                // Original index calculation
                int originalIndex = i * OutputBuckets + j;

                // Transposed index calculation
                int transposedIndex = j * (2 * Layer1Size) + i;

                // Assign value to transposed position
                transposedWeights[transposedIndex] = outputWeights[originalIndex];
            }
        }

        outputWeights = transposedWeights;

        for (var i = 0; i < OutputBiases.Length; i++)
        {
            OutputBiases[i] = reader.ReadInt16();
        }

        // Allocate unmanaged memory
        FeatureWeights = AlignedAllocZeroedShort((nuint)featureWeights.Length);
        FeatureBiases = AlignedAllocZeroedShort((nuint)featureBiases.Length);
        OutputWeights = AlignedAllocZeroedShort((nuint)outputWeights.Length);

        // Copy managed array to unmanaged memory
        fixed (short* sourcePtr = featureWeights)
        {
            Buffer.MemoryCopy(sourcePtr, FeatureWeights, featureWeights.Length * sizeof(short),
                featureWeights.Length * sizeof(short));
        }

        fixed (short* sourcePtr = featureBiases)
        {
            Buffer.MemoryCopy(sourcePtr, FeatureBiases, featureBiases.Length * sizeof(short),
                featureBiases.Length * sizeof(short));
        }

        fixed (short* sourcePtr = outputWeights)
        {
            Buffer.MemoryCopy(sourcePtr, OutputWeights, outputWeights.Length * sizeof(short),
                outputWeights.Length * sizeof(short));
        }
    }

    public static unsafe VectorShort* AlignedAllocZeroedShort(nuint items)
    {
        const nuint alignment = 64;
        var bytes = sizeof(short) * items;
        var block = NativeMemory.AlignedAlloc(bytes, alignment);
        if (block == null)
        {
            throw new OutOfMemoryException("Failed to allocate aligned memory.");
        }

        NativeMemory.Clear(block, bytes);
        return (VectorShort*)block;
    }
    public static unsafe short* AlignedAllocZeroed(nuint items)
    {
        const nuint alignment = 64;
        var bytes = sizeof(short) * items;
        var block = NativeMemory.AlignedAlloc(bytes, alignment);
        if (block == null)
        {
            throw new OutOfMemoryException("Failed to allocate aligned memory.");
        }

        NativeMemory.Clear(block, bytes);
        return (short*)block;
    }

    public static unsafe void Dispose()
    {
        if (FeatureWeights != null)
        {
            Marshal.FreeHGlobal((IntPtr)FeatureWeights);
        }

        if (FeatureBiases != null)
        {
            Marshal.FreeHGlobal((IntPtr)FeatureBiases);
        }

        if (OutputWeights != null)
        {
            Marshal.FreeHGlobal((IntPtr)OutputWeights);
        }
    }
}