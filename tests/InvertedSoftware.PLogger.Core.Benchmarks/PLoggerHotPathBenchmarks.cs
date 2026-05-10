// Copyright (c) Inverted Software. All rights reserved.

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Core.Benchmarks;

[Config(typeof(Config))]
[MemoryDiagnoser]
public class PLoggerHotPathBenchmarks
{
    private sealed class Config : ManualConfig
    {
        public Config()
        {
            AddJob(Job.Default.WithWarmupCount(3).WithIterationCount(7));
            AddDiagnoser(MemoryDiagnoser.Default);
        }
    }

    private PLogger _plogger = null!;
    private ILogger _consoleLogger = null!;
    private ILoggerFactory _consoleFactory = null!;
    private Exception _ex = null!;

    [GlobalSetup]
    public void Setup()
    {
        var channel = new PLoggerChannel(1 << 17);
        var monitor = new StaticOptionsMonitor(new PLoggerOptions());
        _plogger = new PLogger("Bench", monitor, channel, scopeProviderLookup: static () => null);

        _consoleFactory = LoggerFactory.Create(static b =>
        {
            b.ClearProviders();
            b.AddProvider(NullLoggerProvider.Instance);
            b.SetMinimumLevel(LogLevel.Trace);
        });
        _consoleLogger = _consoleFactory.CreateLogger("Bench");

        try { throw new InvalidOperationException("bench"); }
        catch (Exception ex) { _ex = ex; }
    }

    [Benchmark(Baseline = true, Description = "PLogger.LogError(no exception)")]
    public void PLogger_LogError_NoEx() => _plogger.LogError("hello bench");

    [Benchmark(Description = "PLogger.LogError(with exception)")]
    public void PLogger_LogError_WithEx() => _plogger.LogError(_ex, "hello bench");

    [Benchmark(Description = "Microsoft Null logger.LogError(no exception)")]
    public void Microsoft_LogError_NoEx() => _consoleLogger.LogError("hello bench");

    [GlobalCleanup]
    public void Cleanup() => _consoleFactory.Dispose();

    private sealed class StaticOptionsMonitor : IOptionsMonitor<PLoggerOptions>
    {
        public StaticOptionsMonitor(PLoggerOptions value) { CurrentValue = value; }
        public PLoggerOptions CurrentValue { get; }
        public PLoggerOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<PLoggerOptions, string?> listener) => null;
    }
}
