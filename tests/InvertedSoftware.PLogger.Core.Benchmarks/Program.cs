// Copyright (c) Inverted Software. All rights reserved.

using BenchmarkDotNet.Running;

namespace InvertedSoftware.PLogger.Core.Benchmarks;

internal static class Program
{
    public static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
