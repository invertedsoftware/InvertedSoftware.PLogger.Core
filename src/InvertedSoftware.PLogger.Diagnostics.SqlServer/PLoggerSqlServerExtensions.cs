// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using System.Globalization;
using InvertedSoftware.PLogger.Diagnostics;
using InvertedSoftware.PLogger.Diagnostics.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.Logging;

/// <summary>
/// Hangs <c>AddSqlServerStore</c> off <see cref="PLoggerDiagnosticsBuilder"/>.
/// Wires up the schema initializer (idempotent migration) plus the
/// <see cref="IDiagnosticStore"/> implementation.
/// </summary>
public static class PLoggerSqlServerExtensions
{
    public static PLoggerDiagnosticsBuilder AddSqlServerStore(
        this PLoggerDiagnosticsBuilder builder,
        Action<SqlServerDiagnosticsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IConfigureOptions<SqlServerDiagnosticsOptions>, SqlServerConfigureOptions>());
        PLoggerOptionsRegistration.AddConfigurationChangeTokenSource<SqlServerDiagnosticsOptions>(
            services, SqlServerDiagnosticsOptions.SectionName);

        if (configure is not null)
        {
            services.Configure(configure);
        }

        // Replace (not TryAdd) — supersedes the NullDiagnosticStore that
        // AddPLoggerDiagnostics() always registers as the fallback.
        services.Replace(ServiceDescriptor.Singleton<IDiagnosticStore, SqlServerDiagnosticStore>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, SqlServerSchemaInitializer>());

        return builder;
    }

    private sealed class SqlServerConfigureOptions : IConfigureOptions<SqlServerDiagnosticsOptions>
    {
        private readonly IConfiguration _config;
        public SqlServerConfigureOptions(IConfiguration config) { _config = config; }

        public void Configure(SqlServerDiagnosticsOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            var section = _config.GetSection(SqlServerDiagnosticsOptions.SectionName);

            var conn = section["ConnectionString"];
            if (!string.IsNullOrEmpty(conn))
            {
                options.ConnectionString = conn;
            }

            if (bool.TryParse(section["ApplySchemaOnStartup"], out var apply))
            {
                options.ApplySchemaOnStartup = apply;
            }

            if (int.TryParse(section["CommandTimeoutSeconds"], CultureInfo.InvariantCulture, out var timeout)
                && timeout > 0)
            {
                options.CommandTimeoutSeconds = timeout;
            }
        }
    }
}
