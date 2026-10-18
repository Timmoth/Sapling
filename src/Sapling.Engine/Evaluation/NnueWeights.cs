using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Sapling.Engine.Evaluation;
public static unsafe class NnueWeights
{
    public const int InputSize = 768;
    public const int Layer1Size = 1024;

    public const byte InputBuckets = 8;
    public const byte OutputBuckets = 8;
    public static readonly unsafe short* FeatureWeights;
    public static readonly unsafe short* FeatureBiases;
    public static readonly unsafe short* OutputWeights;
    public static readonly unsafe short* OutputBiases;

    public static readonly byte* BucketLayout;


    static unsafe NnueWeights()
    {
        // 8 king buckets, a1=0, horizontally mirrored (file e..h folds onto d..a).
        // Must stay identical to the table the trainer feeds ChessBucketsMirrored;
        // NnueTrainerLayoutTests pins the two together.
        var kingBuckets = new byte[64]
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

        BucketLayout = AlignedAllocZeroedByte(64);
        fixed (byte* kingBucketsPtr = kingBuckets) // Pin the array in memory
        {
            // Copy the data from kingBucketsPtr to BucketLayout
            Buffer.MemoryCopy(kingBucketsPtr, BucketLayout, 64, 64);
        }


        var assembly = typeof(GameState).Assembly;
        var info = assembly.GetName();
        var name = info.Name;
        using var stream = assembly
            .GetManifestResourceStream($"{name}.Resources.sapling.nnue")
            ?? throw new InvalidOperationException("The embedded sapling.nnue network is missing.");

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

        var outputBiases = new short[OutputBuckets];
        for (var i = 0; i < outputBiases.Length; i++)
        {
            outputBiases[i] = reader.ReadInt16();
        }

        //var result = Encoding.UTF8.GetString(reader.ReadBytes((int)(reader.BaseStream.Length - reader.BaseStream.Position)));
        // Console.WriteLine(result);
        
        // Allocate unmanaged memory
        FeatureWeights = AlignedAllocZeroedShort((nuint)featureWeights.Length);
        FeatureBiases = AlignedAllocZeroedShort((nuint)featureBiases.Length);
        OutputWeights = AlignedAllocZeroedShort((nuint)outputWeights.Length);
        OutputBiases = AlignedAllocZeroedShort((nuint)outputBiases.Length);

        // Copy managed array to unmanaged memory
        fixed (short* sourcePtr = outputBiases)
        {
            Buffer.MemoryCopy(sourcePtr, OutputBiases, outputBiases.Length * sizeof(short),
                outputBiases.Length * sizeof(short));
        }

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

    public static unsafe byte* AlignedAllocZeroedByte(nuint items)
    {
        const nuint alignment = 64;
        var bytes = items;
        var block = NativeMemory.AlignedAlloc(bytes, alignment);
        if (block == null)
        {
            throw new OutOfMemoryException("Failed to allocate aligned memory.");
        }

        NativeMemory.Clear(block, bytes);
        return (byte*)block;
    }
    public static unsafe short* AlignedAllocZeroedShort(nuint items)
    {
        const nuint alignment = 64;
        if (items > nuint.MaxValue / sizeof(short))
        {
            throw new ArgumentOutOfRangeException(nameof(items), "Element count exceeds addressable byte size.");
        }

        var bytes = sizeof(short) * items;
        var block = NativeMemory.AlignedAlloc(bytes, alignment);
        if (block == null)
        {
            throw new OutOfMemoryException("Failed to allocate aligned memory.");
        }

        NativeMemory.Clear(block, bytes);
        return (short*)block;
    }

    // Compatibility shim: published pointers are immutable process-wide resources.
    // They cannot be freed while any session may still use them or reinitialize them later.
    [Obsolete("Weights are shared for the runtime lifetime. Dispose engine sessions instead.")]
    public static void Dispose() { }
}
