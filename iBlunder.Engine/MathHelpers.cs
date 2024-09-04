using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace iBlunder.Engine;

public static class MathHelpers
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int VectorSqrt(int input)
    {
        var value = Vector128.Create((float)input);
        var result = Sse.Sqrt(value);
        return (int)result.ToScalar();
    }
}