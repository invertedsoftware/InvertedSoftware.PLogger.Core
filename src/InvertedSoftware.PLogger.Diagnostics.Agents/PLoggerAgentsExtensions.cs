// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using System.Globalization;
using InvertedSoftware.PLogger.Diagnostics;
using InvertedSoftware.PLogger.Diagnostics.Agents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.Logging;

/// <summary>
/// <c>AddPLoggerMultiAgent()</c> chains off the diagnostics builder.
/// Replaces whatever <see cref="IDiagnosticAgent"/> was registered
/// previously (Phase 3's single-prompt one, or the no-op default).
/// </summary>
public static class PLoggerAgentsExtensions
{
    public static PLoggerDiagnosticsBuilder AddPLoggerMultiAgent(
        this PLoggerDiagnosticsBuilder builder,
        Action<PLoggerAgentsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IConfigureOptions<PLoggerAgentsOptions>, PLoggerAgentsConfigureOptions>());
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<TriageAgent>();
        services.TryAddSingleton<ContextAgent>();
        services.TryAddSingleton<DiagnosisAgent>();
        services.TryAddSingleton<FixAgent>();
        services.TryAddSingleton<ValidatorAgent>();

        services.Replace(ServiceDescriptor.Singleton<IDiagnosticAgent, MultiAgentDiagnosisAgent>());

        return builder;
    }

    private sealed class PLoggerAgentsConfigureOptions : IConfigureOptions<PLoggerAgentsOptions>
    {
        private readonly IConfiguration _config;
        public PLoggerAgentsConfigureOptions(IConfiguration config) { _config = config; }

        public void Configure(PLoggerAgentsOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            var section = _config.GetSection(PLoggerAgentsOptions.SectionName);

            var triage = section["TriageModel"];
            if (!string.IsNullOrEmpty(triage))
            {
                options.TriageModel = triage;
            }

            var frontier = section["FrontierModel"];
            if (!string.IsNullOrEmpty(frontier))
            {
                options.FrontierModel = frontier;
            }

            if (int.TryParse(section["MaxTokensPerAgent"], CultureInfo.InvariantCulture, out var max) && max > 0)
            {
                options.MaxTokensPerAgent = max;
            }

            if (int.TryParse(section["FixRetryCount"], CultureInfo.InvariantCulture, out var retry) && retry >= 0)
            {
                options.FixRetryCount = retry;
            }

            if (TimeSpan.TryParse(section["CallTimeout"], CultureInfo.InvariantCulture, out var timeout))
            {
                options.CallTimeout = timeout;
            }
        }
    }
}
