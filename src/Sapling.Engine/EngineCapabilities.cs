using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Sapling.Engine;

/// <summary>
/// Process capabilities and diagnostic backend overrides. Set switches before engine startup.
/// </summary>
public static class EngineCapabilities
{
#if SAPLING_SCALAR
    public const bool ForceScalar = true;
#else
    public static readonly bool ForceScalar = AppContext.TryGetSwitch("Sapling.ForceScalar", out var value) && value;
#endif

    public static readonly string? RequestedBackend;
    public static readonly bool UsePext;

    // Resolved once at startup so the generated NNUE kernels read a cached field instead of
    // re-evaluating the backend switch on every accumulator update / ForwardSCReLU.
    public static readonly bool UseX86Nnue;
    public static readonly bool UseVector128Nnue;

    static EngineCapabilities()
    {
        var backend = Environment.GetEnvironmentVariable("SAPLING_BACKEND");
        if (backend is { Length: > 0 })
        {
            if (ForceScalar && backend != "Scalar")
                throw new NotSupportedException("Forced-scalar builds/switches only accept SAPLING_BACKEND=Scalar");
            RequestedBackend = backend switch
            {
                "Scalar" => "Scalar",
                "Vector128" => Vector128.IsHardwareAccelerated
                    ? "Vector128"
                    : throw new NotSupportedException("SAPLING_BACKEND=Vector128 requires hardware-accelerated Vector128"),
                "Avx2" => Avx2Requested(),
                "Avx512" => Avx512Requested(),
                _ => throw new NotSupportedException($"unknown SAPLING_BACKEND '{backend}' (Scalar|Vector128|Avx2|Avx512)"),
            };
        }

        var forcePext = AppContext.TryGetSwitch("Sapling.ForcePext", out var pextValue) && pextValue;
        if (forcePext && !Bmi2.X64.IsSupported)
            throw new NotSupportedException("Sapling.ForcePext requires BMI2 hardware support");
        var forceMagic = AppContext.TryGetSwitch("Sapling.ForceMagic", out var magicValue) && magicValue;
        if (forcePext && forceMagic)
            throw new NotSupportedException("Sapling.ForcePext and Sapling.ForceMagic are mutually exclusive");

        UsePext = Bmi2.X64.IsSupported && !forceMagic;

        UseX86Nnue = !ForceScalar && RequestedBackend switch
        {
            "Scalar" or "Vector128" => false,
            _ => AvxIntrinsics.IsSupported,
        };
        UseVector128Nnue = !ForceScalar && RequestedBackend != "Scalar" && !UseX86Nnue && Vector128.IsHardwareAccelerated;
    }

    private static string Avx2Requested()
    {
#if AVX512
        throw new NotSupportedException("SAPLING_BACKEND=Avx2 requires a build without the AVX512 define");
#else
        if (!Avx2.IsSupported)
            throw new NotSupportedException("SAPLING_BACKEND=Avx2 requires AVX2 hardware support");
        return "Avx2";
#endif
    }

    private static string Avx512Requested()
    {
#if AVX512
        if (!Avx512BW.IsSupported || !Avx512F.IsSupported)
            throw new NotSupportedException("SAPLING_BACKEND=Avx512 requires AVX512BW/AVX512F hardware support");
        return "Avx512";
#else
        throw new NotSupportedException("SAPLING_BACKEND=Avx512 requires an AVX512 build (AVX512 define)");
#endif
    }

    public static string NnueBackend => RequestedBackend ?? (UseX86Nnue
#if AVX512
        ? "Avx512"
#else
        ? "Avx2"
#endif
        : UseVector128Nnue ? "Vector128" : "Scalar");
}
