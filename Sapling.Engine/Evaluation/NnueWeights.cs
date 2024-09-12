using System.IO;
using System;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;

namespace Sapling.Engine.Evaluation;

public static class NnueWeights
{
    public const int InputSize = 768;
    public const int Layer1Size = 512;

    public const short OutputBuckets = 8;

    public static readonly unsafe short* FeatureWeights;
    public static readonly unsafe short* FeatureBiases;
    public static readonly unsafe short* OutputWeights;
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
        FeatureWeights = AlignedAllocZeroed((nuint)featureWeights.Length);
        FeatureBiases = AlignedAllocZeroed((nuint)featureBiases.Length);
        OutputWeights = AlignedAllocZeroed((nuint)outputWeights.Length);

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