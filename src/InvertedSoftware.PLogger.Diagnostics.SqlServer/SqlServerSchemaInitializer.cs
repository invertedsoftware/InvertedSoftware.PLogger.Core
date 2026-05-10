// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Diagnostics.SqlServer;

/// <summary>
/// Idempotent schema migration runner. Reads the embedded
/// <c>Schema/0001_initial.sql</c> and applies it on host start. Safe to run
/// repeatedly — the script uses <c>IF NOT EXISTS</c> guards.
/// </summary>
public sealed class SqlServerSchemaInitializer : IHostedService
{
    private readonly IOptionsMonitor<SqlServerDiagnosticsOptions> _options;
    private readonly ILogger<SqlServerSchemaInitializer> _logger;

    public SqlServerSchemaInitializer(
        IOptionsMonitor<SqlServerDiagnosticsOptions> options,
        ILogger<SqlServerSchemaInitializer> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var opts = _options.CurrentValue;
        if (!opts.ApplySchemaOnStartup)
        {
            return;
        }

        if (string.IsNullOrEmpty(opts.ConnectionString))
        {
            return;
        }

        try
        {
            await ApplySchemaAsync(opts, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Logger invariant: never bubble. If schema apply fails, the store
            // will fail too and the pipeline service will log per-event errors.
            _logger.LogWarning(ex, "PLogger SQL diagnostic schema initialization failed; continuing without it.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static async Task ApplySchemaAsync(SqlServerDiagnosticsOptions opts, CancellationToken cancellationToken)
    {
        var script = LoadEmbeddedScript("Schema.0001_initial.sql");

        await using var connection = new SqlConnection(opts.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // SqlClient does not split on GO automatically — we batch-split here.
        foreach (var batch in SplitGoBatches(script))
        {
            if (string.IsNullOrWhiteSpace(batch))
            {
                continue;
            }

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = batch;
            cmd.CommandTimeout = Math.Max(opts.CommandTimeoutSeconds, 30);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static string LoadEmbeddedScript(string relativeName)
    {
        var asm = typeof(SqlServerSchemaInitializer).Assembly;
        var name = asm.GetName().Name + "." + relativeName;
        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded script {name} not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static IEnumerable<string> SplitGoBatches(string script)
    {
        // Trivial split on lines that are exactly "GO" (case-insensitive,
        // optionally surrounded by whitespace). Good enough for our DDL.
        var current = new System.Text.StringBuilder();
        foreach (var rawLine in script.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
            }
            else
            {
                current.AppendLine(line);
            }
        }
        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }
}
