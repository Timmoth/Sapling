using Xunit;

namespace Sapling.Engine.Tests;

public class BackendSelectionTests
{
    [Fact]
    public void ReportedBackendMatchesTheExecutableKernelPath()
    {
        var backend = EngineCapabilities.NnueBackend;
        Assert.Equal(backend is "Avx2" or "Avx512", EngineCapabilities.UseX86Nnue);
        Assert.Equal(backend == "Vector128", EngineCapabilities.UseVector128Nnue);
        if (Environment.GetEnvironmentVariable("SAPLING_BACKEND") == "Scalar")
        {
            Assert.Equal("Scalar", backend);
            Assert.False(EngineCapabilities.UseX86Nnue);
            Assert.False(EngineCapabilities.UseVector128Nnue);
        }
    }
}
