// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using System.Data;
using InvertedSoftware.PLogger.Core.Internal;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Core.Sinks;

/// <summary>
/// Async SQL stored-procedure sink. Each write opens a fresh
/// <see cref="SqlConnection"/> from the SqlClient connection pool —
/// reusing a long-lived connection across the drain mixed badly with
/// retry-on-error, leaving the connection in a half-broken state on
/// transient failure. Pool overhead is negligible at our cadence.
/// </summary>
public sealed class SqlLogSink : IPLogSink
{
    private readonly IOptionsMonitor<PLoggerOptions> _options;
    private readonly ILogger<SqlLogSink> _selfLogger;

    public SqlLogSink(IOptionsMonitor<PLoggerOptions> options, ILogger<SqlLogSink> selfLogger)
    {
        _options = options;
        _selfLogger = selfLogger;
    }

    public async ValueTask WriteAsync(LogEvent evt, CancellationToken cancellationToken)
    {
        var opts = _options.CurrentValue;
        var payload = LogEventFormatter.Format(evt, opts.PLogFileMessageTemplate);

        try
        {
            await using var connection = new SqlConnection(opts.StringConnection);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var cmd = connection.CreateCommand();
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = opts.StoredProcedureName;
            cmd.Parameters.Add(new SqlParameter(opts.MessageParameterName, SqlDbType.NVarChar, -1)
            {
                Value = payload,
            });

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            PLoggerLog.DrainEntryFailed(_selfLogger, nameof(SqlLogSink), ex);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
