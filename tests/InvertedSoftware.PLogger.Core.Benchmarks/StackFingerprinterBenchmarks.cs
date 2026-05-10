// Copyright (c) Inverted Software. All rights reserved.

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using InvertedSoftware.PLogger.Core.Fingerprinting;

namespace InvertedSoftware.PLogger.Core.Benchmarks;

[MemoryDiagnoser]
public class StackFingerprinterBenchmarks
{
    private Exception _shallow = null!;
    private Exception _deep = null!;

    [GlobalSetup]
    public void Setup()
    {
        try { ThrowShallow(); }
        catch (Exception ex) { _shallow = ex; }

        try { ThrowDeep(8); }
        catch (Exception ex) { _deep = ex; }
    }

    [Benchmark(Description = "Compute(shallow)")]
    public byte[] Compute_Shallow() => StackFingerprinter.Compute(_shallow);

    [Benchmark(Description = "Compute(deep w/ inner chain)")]
    public byte[] Compute_Deep() => StackFingerprinter.Compute(_deep);

    [Benchmark(Description = "Normalize(deep)")]
    public string Normalize_Deep() => StackFingerprinter.Normalize(_deep);

    private static void ThrowShallow() => throw new InvalidOperationException("shallow");

    private static void ThrowDeep(int depth)
    {
        try
        {
            if (depth <= 0)
            {
                throw new InvalidOperationException("deepest");
            }
            ThrowDeep(depth - 1);
        }
        catch (Exception inner)
        {
            throw new ApplicationException($"depth={depth}", inner);
        }
    }
}
