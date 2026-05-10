// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Diagnostics;

/// <summary>
/// Fluent surface returned from <c>AddPLoggerDiagnostics()</c>. Phase-2+
/// packages (SqlServer, AI, Agents, GitHub, Slack, Teams) hang their
/// extensions off this so registration reads as
/// <c>builder.Logging.AddPLogger().AddPLoggerDiagnostics().AddSqlServerStore()</c>.
/// </summary>
public sealed class PLoggerDiagnosticsBuilder
{
    public PLoggerDiagnosticsBuilder(IServiceCollection services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>The underlying service collection.</summary>
    public IServiceCollection Services { get; }
}

/// <summary>
/// Helpers shared by every package's options registration so they all
/// participate in <see cref="IOptionsMonitor{TOptions}"/> change-token
/// reload uniformly.
/// </summary>
public static class PLoggerOptionsRegistration
{
    /// <summary>
    /// Registers a <see cref="ConfigurationChangeTokenSource{TOptions}"/>
    /// against the named section so <see cref="IOptionsMonitor{TOptions}"/>
    /// re-invokes <see cref="IConfigureOptions{TOptions}"/> when the
    /// underlying configuration source reloads.
    /// </summary>
    /// <remarks>
    /// We can't use <c>TryAddEnumerable</c> with a factory descriptor —
    /// <c>ServiceCollectionDescriptorExtensions</c> requires a concrete
    /// <c>ImplementationType</c> or <c>ImplementationInstance</c> to dedupe.
    /// Plain <c>AddSingleton</c> is acceptable here because each
    /// <c>AddPLogger*</c> extension is the sole registrar for its
    /// options type, and a duplicate (consumer calls the extension twice)
    /// just causes the options to reload twice on a config change —
    /// idempotent, not a correctness issue.
    /// </remarks>
    public static void AddConfigurationChangeTokenSource<TOptions>(
        IServiceCollection services,
        string sectionName)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(sectionName);

        services.AddSingleton<IOptionsChangeTokenSource<TOptions>>(sp =>
            new ConfigurationChangeTokenSource<TOptions>(
                sp.GetRequiredService<IConfiguration>().GetSection(sectionName)));
    }
}
