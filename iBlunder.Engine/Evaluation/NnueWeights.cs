using System.Reflection;
using System.Text;

namespace iBlunder.Engine.Evaluation;

public static class NnueWeights
{
    public const int InputSize = 768;
    public const int Layer1Size = 256;

    public static readonly short[] FeatureWeights = new short[InputSize * Layer1Size];
    public static readonly short[] FeatureBiases = new short[Layer1Size];

    public static readonly short[] OutputWeights = new short[Layer1Size * 2];
    public static readonly short OutputBias;

    static NnueWeights()
    {
        var assembly = Assembly.GetAssembly(typeof(GameState));
        var info = assembly.GetName();
        var name = info.Name;
        using var stream = assembly
            .GetManifestResourceStream($"{name}.Resources.iblunder_weights.nnue")!;

        using var reader = new BinaryReader(stream, Encoding.UTF8, false);
        for (var i = 0; i < FeatureWeights.Length; i++)
        {
            FeatureWeights[i] = reader.ReadInt16();
        }

        for (var i = 0; i < FeatureBiases.Length; i++)
        {
            FeatureBiases[i] = reader.ReadInt16();
        }

        for (var i = 0; i < OutputWeights.Length; i++)
        {
            OutputWeights[i] = reader.ReadInt16();
        }

        OutputBias = reader.ReadInt16();
    }
}