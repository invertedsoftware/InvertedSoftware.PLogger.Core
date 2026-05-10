// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using System.Globalization;
using InvertedSoftware.PLogger.Diagnostics;
using InvertedSoftware.PLogger.Diagnostics.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.Logging;

/// <summary>
/// <c>AddPLoggerAi()</c> chains off the diagnostics builder. Caller is
/// responsible for separately registering an <c>IChatClient</c> via
/// Microsoft.Extensions.AI (e.g. <c>services.AddChatClient(client =>
/// client.UseOpenAI(...))</c>) — we don't tie the framework to any
/// specific provider package.
/// </summary>
public static class PLoggerAiExtensions
{
    public static PLoggerDiagnosticsBuilder AddPLoggerAi(
        this PLoggerDiagnosticsBuilder builder,
        Action<PLoggerAiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IConfigureOptions<PLoggerAiOptions>, PLoggerAiConfigureOptions>());
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<ITokenBudgetEnforcer, TokenBudgetEnforcer>();

        // Replace the no-op agent with the real LLM-backed one. The caller
        // must have registered an IChatClient — DI resolution fails fast at
        // first diagnose call if not (we don't auto-register a provider).
        services.Replace(ServiceDescriptor.Singleton<IDiagnosticAgent, AiDiagnosisAgent>());

        return builder;
    }

    private sealed class PLoggerAiConfigureOptions : IConfigureOptions<PLoggerAiOptions>
    {
        private readonly IConfiguration _config;
        public PLoggerAiConfigureOptions(IConfiguration config) { _config = config; }

        public void Configure(PLoggerAiOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            var section = _config.GetSection(PLoggerAiOptions.SectionName);

            if (bool.TryParse(section["Enabled"], out var enabled))
            {
                options.Enabled = enabled;
            }

            var model = section["Model"];
            if (!string.IsNullOrEmpty(model))
            {
                options.Model = model;
            }

            if (int.TryParse(section["TokenBudgetPerMinute"], CultureInfo.InvariantCulture, out var budget) && budget >= 0)
            {
                options.TokenBudgetPerMinute = budget;
            }

            if (int.TryParse(section["MaxTokensPerCall"], CultureInfo.InvariantCulture, out var maxTok) && maxTok > 0)
            {
                options.MaxTokensPerCall = maxTok;
            }

            if (double.TryParse(section["VectorSimilarityThreshold"], CultureInfo.InvariantCulture, out var thresh)
                && thresh is >= 0 and <= 1)
            {
                options.VectorSimilarityThreshold = thresh;
            }

            if (TimeSpan.TryParse(section["CallTimeout"], CultureInfo.InvariantCulture, out var timeout))
            {
                options.CallTimeout = timeout;
            }
        }
    }
}
