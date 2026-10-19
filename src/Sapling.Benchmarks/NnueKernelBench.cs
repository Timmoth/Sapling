using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using Sapling.Engine.Evaluation;
using Sapling.Engine.Search;

namespace Sapling.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 8)]
public unsafe class NnueKernelBench
{
    private short* _us;
    private short* _them;
    private short* _source;
    private short* _dest;
    private short* _sub1;
    private short* _sub2;
    private short* _add1;
    private short* _add2;

    [GlobalSetup]
    public void Setup()
    {
        var bytes = (nuint)(NnueWeights.Layer1Size * sizeof(short));
        _us = (short*)NativeMemory.Alloc(bytes);
        _them = (short*)NativeMemory.Alloc(bytes);
        _source = (short*)NativeMemory.Alloc(bytes);
        _dest = (short*)NativeMemory.Alloc(bytes);
        _sub1 = (short*)NativeMemory.Alloc(bytes);
        _sub2 = (short*)NativeMemory.Alloc(bytes);
        _add1 = (short*)NativeMemory.Alloc(bytes);
        _add2 = (short*)NativeMemory.Alloc(bytes);

        var random = new Random(1234);
        for (var i = 0; i < NnueWeights.Layer1Size; i++)
        {
            _us[i] = (short)random.Next(-32, 288);
            _them[i] = (short)random.Next(-32, 288);
            _source[i] = (short)random.Next(short.MinValue, short.MaxValue);
            _sub1[i] = (short)random.Next(short.MinValue, short.MaxValue);
            _sub2[i] = (short)random.Next(short.MinValue, short.MaxValue);
            _add1[i] = (short)random.Next(short.MinValue, short.MaxValue);
            _add2[i] = (short)random.Next(short.MinValue, short.MaxValue);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        NativeMemory.Free(_us);
        NativeMemory.Free(_them);
        NativeMemory.Free(_source);
        NativeMemory.Free(_dest);
        NativeMemory.Free(_sub1);
        NativeMemory.Free(_sub2);
        NativeMemory.Free(_add1);
        NativeMemory.Free(_add2);
    }

    [Benchmark]
    public int ForwardScRelu() => Searcher.ForwardSCReLUProbe(_us, _them, bucket: 3);

    [Benchmark]
    public short SubAdd()
    {
        Searcher.SubAdd(_source, _dest, _sub1, _add1);
        return _dest[0];
    }

    [Benchmark]
    public short SubSubAdd()
    {
        Searcher.SubSubAdd(_source, _dest, _sub1, _sub2, _add1);
        return _dest[0];
    }

    [Benchmark]
    public short SubSubAddAdd()
    {
        Searcher.SubSubAddAdd(_source, _dest, _sub1, _sub2, _add1, _add2);
        return _dest[0];
    }
}
