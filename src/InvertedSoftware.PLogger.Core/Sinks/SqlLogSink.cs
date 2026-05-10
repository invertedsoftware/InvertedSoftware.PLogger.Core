// Copyright (c) Inverted Software. All rights reserved.

using System.Data;
using InvertedSoftware.PLogger.Core.Internal;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Core.Sinks;

/// <summary>
/// Async SQL stored-procedure sink. Reuses a single connection in an
/// open/idle pattern; transient failures are logged and skipped.
/// </summary>
public sealed class SqlLogSink : IPLogSink
{
    private readonly IOptionsMonitor<PLoggerOptions> _options;
    private readonly ILogger<SqlLogSink> _selfLogger;
    private readonly SqlConnection _connection;

    public SqlLogSink(IOptionsMonitor<PLoggerOptions> options, ILogger<SqlLogSink> selfLogger)
    {
        _options = options;
        _selfLogger = selfLogger;
        _connection = new SqlConnection(options.CurrentValue.StringConnection);
    }

    public async ValueTask WriteAsync(LogEvent evt, CancellationToken cancellationToken)
    {
        var opts = _options.CurrentValue;
        var payload = LogEventFormatter.Format(evt, opts.PLogFileMessageTemplate);

        try
        {
            if (_connection.State != ConnectionState.Open)
            {
                await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var cmd = _connection.CreateCommand();
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = opts.StoredProcedureName;
            cmd.Parameters.Add(new SqlParameter(opts.MessageParameterName, SqlDbType.NVarChar, -1)
            {
                Value = payload,
            });

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            PLoggerLog.DrainEntryFailed(_selfLogger, nameof(SqlLogSink), ex);
            try
            {
                if (_connection.State != ConnectionState.Closed)
                {
                    await _connection.CloseAsync().ConfigureAwait(false);
                }
            }
            catch
            {
                // ignored
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // ignored — drain invariant
        }
    }
}
