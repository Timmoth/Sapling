using System.Reflection;
using System.Text;
using Sapling.Engine.MoveGen;

namespace Sapling.Engine.Tuning;

/// <summary>
/// Small, dependency-free float32 inference for the trained human move policy. The fixed model
/// weights are embedded in Sapling.Engine, so native and WebAssembly builds need no ONNX runtime or
/// external model file.
/// </summary>
public static class HumanMovePolicy
{
    private const int BoardLength = HumanPolicyEncoding.BoardChannels * HumanPolicyEncoding.SquaresPerBoard;
    private const int CandidateFeatures = HumanPolicyEncoding.MoveFeatures;
    private const int ConvChannels = 32;
    private const int BoardEmbedding = 128;
    private const int MoveHidden = 64;
    private const string ResourceName = "Sapling.Engine.HumanPolicy.weights";
    private static readonly Weights Model = LoadWeights();

    /// <summary>Returns one raw policy logit per candidate; the input feature layout is schema v1.</summary>
    public static float[] ScoreCandidates(ReadOnlySpan<float> board, ReadOnlySpan<float> moves,
        int candidateCount, int rating)
    {
        if (board.Length != BoardLength)
            throw new ArgumentException($"Board tensor must have length {BoardLength}.", nameof(board));
        if (candidateCount < 1 || moves.Length != checked(candidateCount * CandidateFeatures))
            throw new ArgumentException("Move tensor size does not match the candidate count.", nameof(moves));
        if (rating is < HumanPolicyEncoding.MinRating or > HumanPolicyEncoding.MaxRating)
            throw new ArgumentOutOfRangeException(nameof(rating));

        var first = new float[ConvChannels * 64];
        var second = new float[ConvChannels * 64];
        var embedding = new float[BoardEmbedding];
        Convolve(board, Model.Conv1Weight, Model.Conv1Bias, 18, first);
        Convolve(first, Model.Conv2Weight, Model.Conv2Bias, ConvChannels, second);
        DenseRelu(second, Model.BoardWeight, Model.BoardBias, embedding);

        var ratingFeature = HumanPolicyEncoding.EncodeRating(rating);
        var logits = new float[candidateCount];
        Span<float> hidden = stackalloc float[MoveHidden];
        for (var candidate = 0; candidate < candidateCount; candidate++)
        {
            var move = moves.Slice(candidate * CandidateFeatures, CandidateFeatures);
            for (var unit = 0; unit < MoveHidden; unit++)
            {
                var value = Model.MoveBias[unit];
                var weightOffset = unit * (BoardEmbedding + CandidateFeatures + 1);
                for (var i = 0; i < BoardEmbedding; i++)
                    value += Model.MoveWeight[weightOffset + i] * embedding[i];
                for (var i = 0; i < CandidateFeatures; i++)
                    value += Model.MoveWeight[weightOffset + BoardEmbedding + i] * move[i];
                value += Model.MoveWeight[weightOffset + BoardEmbedding + CandidateFeatures] * ratingFeature;
                hidden[unit] = Math.Max(0f, value);
            }

            var logit = Model.OutputBias[0];
            for (var i = 0; i < MoveHidden; i++)
                logit += Model.OutputWeight[i] * hidden[i];
            logits[candidate] = logit;
        }
        return logits;
    }

    /// <summary>Samples one candidate from the policy softmax and reports its probability.</summary>
    public static int Sample(Random random, ReadOnlySpan<float> board, ReadOnlySpan<float> moves,
        int candidateCount, int rating, out float probability)
    {
        ArgumentNullException.ThrowIfNull(random);
        var logits = ScoreCandidates(board, moves, candidateCount, rating);
        if (candidateCount == 1)
        {
            probability = 1f;
            return 0;
        }

        var max = logits.Max();
        var total = 0.0;
        for (var i = 0; i < logits.Length; i++)
            total += Math.Exp((double)logits[i] - max);
        if (!double.IsFinite(total) || total <= 0)
            throw new InvalidOperationException("Human policy scores cannot be normalized.");

        var target = random.NextDouble() * total;
        var selected = logits.Length - 1;
        for (var i = 0; i < logits.Length; i++)
        {
            var weight = Math.Exp((double)logits[i] - max);
            target -= weight;
            if (target < 0)
            {
                selected = i;
                break;
            }
        }
        probability = (float)(Math.Exp((double)logits[selected] - max) / total);
        return selected;
    }

    private static void Convolve(ReadOnlySpan<float> input, float[] weights, float[] bias,
        int inputChannels, Span<float> output)
    {
        var outputOffset = 0;
        for (var outputChannel = 0; outputChannel < ConvChannels; outputChannel++)
        for (var y = 0; y < 8; y++)
        for (var x = 0; x < 8; x++)
        {
            var sum = bias[outputChannel];
            for (var inputChannel = 0; inputChannel < inputChannels; inputChannel++)
            for (var ky = 0; ky < 3; ky++)
            {
                var sourceY = y + ky - 1;
                if ((uint)sourceY >= 8) continue;
                var sourceBase = inputChannel * 64 + sourceY * 8;
                var weightBase = ((outputChannel * inputChannels + inputChannel) * 3 + ky) * 3;
                for (var kx = 0; kx < 3; kx++)
                {
                    var sourceX = x + kx - 1;
                    if ((uint)sourceX < 8)
                        sum += input[sourceBase + sourceX] * weights[weightBase + kx];
                }
            }
            output[outputOffset++] = Math.Max(0f, sum);
        }
    }

    private static void DenseRelu(ReadOnlySpan<float> input, float[] weights, float[] bias,
        Span<float> output)
    {
        for (var unit = 0; unit < output.Length; unit++)
        {
            var sum = bias[unit];
            var offset = unit * input.Length;
            for (var i = 0; i < input.Length; i++)
                sum += weights[offset + i] * input[i];
            output[unit] = Math.Max(0f, sum);
        }
    }

    private static Weights LoadWeights()
    {
        using var stream = typeof(HumanMovePolicy).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException($"Embedded human policy resource '{ResourceName}' was not found.");
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);
        if (!reader.ReadBytes(8).AsSpan().SequenceEqual("SAPHPW01"u8))
            throw new InvalidDataException("Embedded human policy has an invalid header.");
        if (reader.ReadUInt32() != 1)
            throw new InvalidDataException("Embedded human policy schema version is unsupported.");
        if (reader.ReadUInt32() != 10)
            throw new InvalidDataException("Embedded human policy tensor count is invalid.");

        var conv1Weight = ReadTensor(reader, "board_encoder.0.weight", 32, 18, 3, 3);
        var conv1Bias = ReadTensor(reader, "board_encoder.0.bias", 32);
        var conv2Weight = ReadTensor(reader, "board_encoder.2.weight", 32, 32, 3, 3);
        var conv2Bias = ReadTensor(reader, "board_encoder.2.bias", 32);
        var boardWeight = ReadTensor(reader, "board_encoder.5.weight", 128, 2048);
        var boardBias = ReadTensor(reader, "board_encoder.5.bias", 128);
        var moveWeight = ReadTensor(reader, "move_scorer.0.weight", 64, 263);
        var moveBias = ReadTensor(reader, "move_scorer.0.bias", 64);
        var outputWeight = ReadTensor(reader, "move_scorer.2.weight", 1, 64);
        var outputBias = ReadTensor(reader, "move_scorer.2.bias", 1);
        if (stream.Position != stream.Length)
            throw new InvalidDataException("Embedded human policy contains trailing data.");
        return new Weights(conv1Weight, conv1Bias, conv2Weight, conv2Bias,
            boardWeight, boardBias, moveWeight, moveBias, outputWeight, outputBias);
    }

    private static float[] ReadTensor(BinaryReader reader, string expectedName, params int[] expectedShape)
    {
        var nameLength = reader.ReadByte();
        var nameBytes = reader.ReadBytes(nameLength);
        if (nameBytes.Length != nameLength || Encoding.ASCII.GetString(nameBytes) != expectedName)
            throw new InvalidDataException($"Embedded human policy expected tensor '{expectedName}'.");
        var rank = reader.ReadByte();
        if (rank != expectedShape.Length)
            throw new InvalidDataException($"Embedded human policy tensor '{expectedName}' has an invalid rank.");
        var count = 1;
        foreach (var dimension in expectedShape)
        {
            if (reader.ReadUInt32() != dimension)
                throw new InvalidDataException($"Embedded human policy tensor '{expectedName}' has an invalid shape.");
            count = checked(count * dimension);
        }
        if (reader.ReadUInt32() != count)
            throw new InvalidDataException($"Embedded human policy tensor '{expectedName}' has an invalid value count.");
        var result = new float[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = reader.ReadSingle();
            if (!float.IsFinite(result[i]))
                throw new InvalidDataException($"Embedded human policy tensor '{expectedName}' is not finite.");
        }
        return result;
    }

    private sealed record Weights(float[] Conv1Weight, float[] Conv1Bias, float[] Conv2Weight,
        float[] Conv2Bias, float[] BoardWeight, float[] BoardBias, float[] MoveWeight,
        float[] MoveBias, float[] OutputWeight, float[] OutputBias);
}
