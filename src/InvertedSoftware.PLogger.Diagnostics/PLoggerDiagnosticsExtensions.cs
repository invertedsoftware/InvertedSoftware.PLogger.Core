// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using System.Globalization;
using InvertedSoftware.PLogger.Core;
using InvertedSoftware.PLogger.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

// Surface lives under Microsoft.Extensions.Logging so callers don't need an
// extra using-directive when they're already configuring logging.
namespace Microsoft.Extensions.Logging;

/// <summary>
/// <c>AddPLoggerDiagnostics()</c> extension. Strictly opt-in — calling it is
/// the only way Diagnostics components reach DI. Without this call, the
/// behavior of a host that references the Diagnostics package is identical
/// to one that does not.
/// </summary>
public static class PLoggerDiagnosticsExtensions
{
    /// <summary>
    /// Registers the diagnostic capture sink + pipeline service. Returns a
    /// <see cref="PLoggerDiagnosticsBuilder"/> so callers can chain
    /// <c>.AddSqlServerStore()</c> / <c>.AddOpenAi()</c> etc.
    /// </summary>
    public static PLoggerDiagnosticsBuilder AddPLoggerDiagnostics(
        this ILoggingBuilder builder,
        Action<PLoggerDiagnosticsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<PLoggerDiagnosticsOptions>, DiagnosticsConfigureOptions>());

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<DiagnosticChannel>(static sp =>
        {
            var coreOpts = sp.GetRequiredService<IOptionsMonitor<PLoggerOptions>>().CurrentValue;
            var diagOpts = sp.GetRequiredService<IOptionsMonitor<PLoggerDiagnosticsOptions>>().CurrentValue;
            var capacity = diagOpts.ChannelCapacityOverride ?? coreOpts.DiagnosticChannelCapacity;
            return new DiagnosticChannel(capacity);
        });

        // Default replaceable bindings — Phase 3 (.AI) replaces the agent
        // and enricher; storage providers (.SqlServer) replace the store via
        // ServiceCollection.Replace; Phase 6 (.GitHub etc.) adds notifiers.
        services.TryAddSingleton<IDiagnosticEnricher, DefaultDiagnosticEnricher>();
        services.TryAddSingleton<IDiagnosticAgent, NoOpDiagnosticAgent>();
        services.TryAddSingleton<IDiagnosticStore, NullDiagnosticStore>();
        services.TryAddSingleton<IDiagnosticPipeline, DefaultDiagnosticPipeline>();

        // Capture sink joins the existing IPLogSink fan-out. The drain in Core
        // delivers every event to it; the sink filters by severity + presence
        // of an exception before queuing onto the diagnostic channel.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPLogSink, DiagnosticCaptureSink>());

        // The pipeline runs as a hosted service.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DiagnosticPipelineService>());

        return new PLoggerDiagnosticsBuilder(services);
    }

    /// <summary>
    /// Hand-rolled binding of <c>Logging:PLogger:Diagnostics</c> — AOT-safe,
    /// no <c>ConfigurationBinder.Bind</c> reflection.
    /// </summary>
    private sealed class DiagnosticsConfigureOptions : IConfigureOptions<PLoggerDiagnosticsOptions>
    {
        private readonly IConfiguration _config;

        public DiagnosticsConfigureOptions(IConfiguration config) { _config = config; }

        public void Configure(PLoggerDiagnosticsOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            var section = _config.GetSection(PLoggerDiagnosticsOptions.SectionName);

            if (bool.TryParse(section["Enabled"], out var enabled))
            {
                options.Enabled = enabled;
            }

            var app = section["AppName"];
            if (!string.IsNullOrEmpty(app))
            {
                options.AppName = app;
            }

            var env = section["Environment"];
            if (!string.IsNullOrEmpty(env))
            {
                options.Environment = env;
            }

            if (int.TryParse(section["ChannelCapacityOverride"], CultureInfo.InvariantCulture, out var cap)
                && cap > 0)
            {
                options.ChannelCapacityOverride = cap;
            }

            var minSev = section["MinSeverity"];
            if (!string.IsNullOrEmpty(minSev) && Enum.TryParse<LogLevel>(minSev, ignoreCase: true, out var lvl))
            {
                options.MinSeverity = lvl;
            }

            var timeout = section["ShutdownTimeout"];
            if (!string.IsNullOrEmpty(timeout) && TimeSpan.TryParse(timeout, CultureInfo.InvariantCulture, out var ts))
            {
                options.ShutdownTimeout = ts;
            }
        }
    }
}
