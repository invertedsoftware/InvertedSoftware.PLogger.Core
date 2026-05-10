// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using Microsoft.Extensions.DependencyInjection;

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
