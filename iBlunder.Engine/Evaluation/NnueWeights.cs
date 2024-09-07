using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace iBlunder.Engine.Evaluation;

public static class NnueWeights
{
    public const int InputSize = 768;
    public const int Layer1Size = 256;

    public static readonly short OutputBias;

    public static readonly unsafe short* FeatureWeights;
    public static readonly unsafe short* FeatureBiases;
    public static readonly unsafe short* OutputWeights;

    static unsafe NnueWeights()
    {
        var assembly = Assembly.GetAssembly(typeof(GameState));
        var info = assembly.GetName();
        var name = info.Name;
        using var stream = assembly
            .GetManifestResourceStream($"{name}.Resources.iblunder_weights.nnue")!;

        var featureWeights = new short[InputSize * Layer1Size];
        var featureBiases = new short[Layer1Size];
        var outputWeights = new short[Layer1Size * 2];

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

        OutputBias = reader.ReadInt16();

        // Allocate unmanaged memory
        FeatureWeights = AlignedAllocZeroed(InputSize * Layer1Size);
        FeatureBiases = AlignedAllocZeroed(Layer1Size);
        OutputWeights = AlignedAllocZeroed(Layer1Size * 2);

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