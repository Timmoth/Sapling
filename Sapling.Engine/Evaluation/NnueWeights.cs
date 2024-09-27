using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Sapling.Engine.Evaluation;
public static class NnueWeights
{
    public const int InputSize = 768;
    public const int Layer1Size = 1024;

    public const byte InputBuckets = 8;
    public const byte OutputBuckets = 8;
    public static readonly unsafe VectorShort* FeatureWeights;
    public static readonly unsafe VectorShort* FeatureBiases;
    public static readonly unsafe VectorShort* OutputWeights;
    public static readonly short[] OutputBiases = new short[OutputBuckets];

    //public static readonly byte[] BucketLayout = new byte[64]
    //{
    //    0, 0, 1, 1, 1, 1, 0, 0,
    //    2, 2, 2, 2, 2, 2, 2, 2,
    //    3, 3, 3, 3, 3, 3, 3, 3,
    //    3, 3, 3, 3, 3, 3, 3, 3,
    //    3, 3, 3, 3, 3, 3, 3, 3,
    //    3, 3, 3, 3, 3, 3, 3, 3,
    //    3, 3, 3, 3, 3, 3, 3, 3,
    //    3, 3, 3, 3, 3, 3, 3, 3,
    //};

    public static byte[] BucketLayout = new byte[64]
    {
        0, 1, 2, 3, 3, 2, 1, 0,
        4, 4, 5, 5, 5, 5, 4, 4,
        6, 6, 6, 6, 6, 6, 6, 6,
        6, 6, 6, 6, 6, 6, 6, 6,
        7, 7, 7, 7, 7, 7, 7, 7,
        7, 7, 7, 7, 7, 7, 7, 7,
        7, 7, 7, 7, 7, 7, 7, 7,
        7, 7, 7, 7, 7, 7, 7, 7,
    };

    static unsafe NnueWeights()
    {
        var assembly = Assembly.GetAssembly(typeof(GameState));
        var info = assembly.GetName();
        var name = info.Name;
        using var stream = assembly
            .GetManifestResourceStream($"{name}.Resources.sapling.nnue")!;

        var featureWeights = new short[InputSize * Layer1Size * InputBuckets];
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
        for (var i = 0; i < 2 * Layer1Size; i++)
        {
            for (var j = 0; j < OutputBuckets; j++)
            {
                // Original index calculation
                var originalIndex = i * OutputBuckets + j;

                // Transposed index calculation
                var transposedIndex = j * 2 * Layer1Size + i;

                // Assign value to transposed position
                transposedWeights[transposedIndex] = outputWeights[originalIndex];
            }
        }

        outputWeights = transposedWeights;

        for (var i = 0; i < OutputBiases.Length; i++)
        {
            OutputBiases[i] = reader.ReadInt16();
        }

        //var result = Encoding.UTF8.GetString(reader.ReadBytes((int)(reader.BaseStream.Length - reader.BaseStream.Position)));
        // Console.WriteLine(result);
        
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