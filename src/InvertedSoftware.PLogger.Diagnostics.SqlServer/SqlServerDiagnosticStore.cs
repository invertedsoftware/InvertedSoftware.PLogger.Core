// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Diagnostics.SqlServer;

/// <summary>
/// SQL Server 2025 implementation of <see cref="IDiagnosticStore"/>.
/// Fingerprint-keyed dedup with occurrence counting; vector similarity is
/// stubbed in Phase 2 (returns empty list) and lit up in Phase 3 once
/// embeddings are available.
/// </summary>
public sealed class SqlServerDiagnosticStore : IDiagnosticStore
{
    private static readonly IReadOnlyList<DiagnosticRecord> EmptySimilar = Array.Empty<DiagnosticRecord>();

    private readonly IOptionsMonitor<SqlServerDiagnosticsOptions> _sqlOptions;
    private readonly IOptionsMonitor<PLoggerDiagnosticsOptions> _diagOptions;
    private readonly ILogger<SqlServerDiagnosticStore> _logger;

    public SqlServerDiagnosticStore(
        IOptionsMonitor<SqlServerDiagnosticsOptions> sqlOptions,
        IOptionsMonitor<PLoggerDiagnosticsOptions> diagOptions,
        ILogger<SqlServerDiagnosticStore> logger)
    {
        _sqlOptions = sqlOptions;
        _diagOptions = diagOptions;
        _logger = logger;
    }

    public async ValueTask<DiagnosticRecord> UpsertAsync(
        DiagnosticContext context,
        DiagnosisResult? diagnosis,
        CancellationToken cancellationToken)
    {
        var sqlOpts = _sqlOptions.CurrentValue;
        var diagOpts = _diagOptions.CurrentValue;
        var evt = context.Event;
        var ex = evt.Exception ?? throw new InvalidOperationException("DiagnosticContext.Event must carry an exception.");

        await using var connection = new SqlConnection(sqlOpts.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // MERGE on the unique (AppName, Environment, Fingerprint) index.
        // OUTPUT inserts.* and updated.* into a table variable so we can
        // return the persisted record + its Id (whether it was new or hit).
        const string mergeSql = @"
DECLARE @Now DATETIME2(3) = SYSUTCDATETIME();

MERGE plogger.Diagnostics WITH (HOLDLOCK) AS target
USING (SELECT @AppName AS AppName, @Environment AS Environment, @Fingerprint AS Fingerprint) AS src
   ON target.AppName = src.AppName
  AND target.Environment = src.Environment
  AND target.Fingerprint = src.Fingerprint
WHEN MATCHED THEN
    UPDATE SET
        OccurrenceCount = OccurrenceCount + 1,
        LastSeenUtc = @Now,
        ExceptionMessage = @ExceptionMessage,
        Diagnosis = COALESCE(@Diagnosis, target.Diagnosis)
WHEN NOT MATCHED THEN
    INSERT (Fingerprint, FirstSeenUtc, LastSeenUtc, OccurrenceCount,
            AppName, Environment, ExceptionType, ExceptionMessage,
            NormalizedStack, Diagnosis, ResolutionState)
    VALUES (@Fingerprint, @Now, @Now, 1,
            @AppName, @Environment, @ExceptionType, @ExceptionMessage,
            @NormalizedStack, @Diagnosis, 0)
OUTPUT inserted.Id,
       inserted.Fingerprint, inserted.FirstSeenUtc, inserted.LastSeenUtc,
       inserted.OccurrenceCount, inserted.AppName, inserted.Environment,
       inserted.ExceptionType, inserted.ExceptionMessage, inserted.NormalizedStack,
       inserted.SourceFile, inserted.SourceLine, inserted.Diagnosis,
       inserted.ResolutionState, inserted.GitHubIssueUrl, inserted.AppliedCommitSha;
";
        DiagnosticRecord persisted;
        await using (var cmd = new SqlCommand(mergeSql, connection))
        {
            cmd.CommandTimeout = Math.Max(sqlOpts.CommandTimeoutSeconds, 1);
            cmd.Parameters.Add(new SqlParameter("@Fingerprint", SqlDbType.Binary, 16) { Value = context.Fingerprint.Hash });
            cmd.Parameters.Add(new SqlParameter("@AppName", SqlDbType.NVarChar, 200) { Value = diagOpts.AppName });
            cmd.Parameters.Add(new SqlParameter("@Environment", SqlDbType.NVarChar, 50) { Value = diagOpts.Environment });
            cmd.Parameters.Add(new SqlParameter("@ExceptionType", SqlDbType.NVarChar, 500) { Value = ex.GetType().FullName ?? ex.GetType().Name });
            cmd.Parameters.Add(new SqlParameter("@ExceptionMessage", SqlDbType.NVarChar, -1) { Value = ex.Message });
            cmd.Parameters.Add(new SqlParameter("@NormalizedStack", SqlDbType.NVarChar, -1) { Value = context.Fingerprint.NormalizedStack });
            cmd.Parameters.Add(new SqlParameter("@Diagnosis", SqlDbType.NVarChar, -1)
            {
                Value = (object?)diagnosis?.ToString() ?? DBNull.Value,
            });

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("MERGE returned no rows.");
            }

            persisted = MapRecord(reader);
        }

        await InsertOccurrenceAsync(connection, persisted.Id, evt, sqlOpts, cancellationToken).ConfigureAwait(false);
        return persisted;
    }

    public async ValueTask<DiagnosticRecord?> GetByFingerprintAsync(byte[] fingerprint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        var sqlOpts = _sqlOptions.CurrentValue;
        var diagOpts = _diagOptions.CurrentValue;

        await using var connection = new SqlConnection(sqlOpts.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = new SqlCommand(@"
SELECT TOP 1 Id, Fingerprint, FirstSeenUtc, LastSeenUtc, OccurrenceCount,
       AppName, Environment, ExceptionType, ExceptionMessage, NormalizedStack,
       SourceFile, SourceLine, Diagnosis, ResolutionState, GitHubIssueUrl, AppliedCommitSha
  FROM plogger.Diagnostics
 WHERE AppName = @AppName AND Environment = @Environment AND Fingerprint = @Fingerprint;
", connection);
        cmd.CommandTimeout = sqlOpts.CommandTimeoutSeconds;
        cmd.Parameters.Add(new SqlParameter("@AppName", SqlDbType.NVarChar, 200) { Value = diagOpts.AppName });
        cmd.Parameters.Add(new SqlParameter("@Environment", SqlDbType.NVarChar, 50) { Value = diagOpts.Environment });
        cmd.Parameters.Add(new SqlParameter("@Fingerprint", SqlDbType.Binary, 16) { Value = fingerprint });

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }
        return MapRecord(reader);
    }

    public ValueTask<IReadOnlyList<DiagnosticRecord>> FindSimilarAsync(
        ReadOnlyMemory<float> embedding,
        double threshold,
        int limit,
        CancellationToken cancellationToken)
    {
        // Phase 2 stub. Phase 3 fills this in with VECTOR_DISTANCE once
        // embeddings are populated by Diagnostics.AI.
        return ValueTask.FromResult(EmptySimilar);
    }

    public async ValueTask MarkFixAppliedAsync(byte[] fingerprint, string commitSha, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentException.ThrowIfNullOrEmpty(commitSha);
        var sqlOpts = _sqlOptions.CurrentValue;
        var diagOpts = _diagOptions.CurrentValue;

        await using var connection = new SqlConnection(sqlOpts.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new SqlCommand(@"
UPDATE plogger.Diagnostics
   SET AppliedCommitSha = @CommitSha,
       ResolutionState = 3 -- Applied
 WHERE AppName = @AppName AND Environment = @Environment AND Fingerprint = @Fingerprint;
", connection);
        cmd.CommandTimeout = sqlOpts.CommandTimeoutSeconds;
        cmd.Parameters.Add(new SqlParameter("@AppName", SqlDbType.NVarChar, 200) { Value = diagOpts.AppName });
        cmd.Parameters.Add(new SqlParameter("@Environment", SqlDbType.NVarChar, 50) { Value = diagOpts.Environment });
        cmd.Parameters.Add(new SqlParameter("@Fingerprint", SqlDbType.Binary, 16) { Value = fingerprint });
        cmd.Parameters.Add(new SqlParameter("@CommitSha", SqlDbType.NVarChar, 64) { Value = commitSha });
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertOccurrenceAsync(
        SqlConnection connection,
        long diagnosticId,
        Core.LogEvent evt,
        SqlServerDiagnosticsOptions sqlOpts,
        CancellationToken cancellationToken)
    {
        await using var cmd = new SqlCommand(@"
INSERT INTO plogger.DiagnosticOccurrences (DiagnosticId, OccurredUtc, TraceId, SpanId)
VALUES (@DiagnosticId, @OccurredUtc, @TraceId, @SpanId);
", connection);
        cmd.CommandTimeout = sqlOpts.CommandTimeoutSeconds;
        cmd.Parameters.Add(new SqlParameter("@DiagnosticId", SqlDbType.BigInt) { Value = diagnosticId });
        cmd.Parameters.Add(new SqlParameter("@OccurredUtc", SqlDbType.DateTime2) { Value = evt.TimestampUtc.UtcDateTime });
        cmd.Parameters.Add(new SqlParameter("@TraceId", SqlDbType.NVarChar, 64) { Value = (object?)evt.TraceId ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@SpanId", SqlDbType.NVarChar, 32) { Value = (object?)evt.SpanId ?? DBNull.Value });
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static DiagnosticRecord MapRecord(SqlDataReader reader)
    {
        // Column order matches both queries.
        var id = reader.GetInt64(0);
        var fingerprint = (byte[])reader.GetValue(1);
        var firstSeen = new DateTimeOffset(reader.GetDateTime(2), TimeSpan.Zero);
        var lastSeen = new DateTimeOffset(reader.GetDateTime(3), TimeSpan.Zero);
        var occurrence = reader.GetInt64(4);
        var appName = reader.GetString(5);
        var environment = reader.GetString(6);
        var exceptionType = reader.GetString(7);
        var exceptionMessage = reader.GetString(8);
        var normalizedStack = reader.GetString(9);
        var sourceFile = reader.IsDBNull(10) ? null : reader.GetString(10);
        var sourceLine = reader.IsDBNull(11) ? (int?)null : reader.GetInt32(11);
        var diagnosis = reader.IsDBNull(12) ? null : reader.GetString(12);
        var resolution = (ResolutionState)reader.GetByte(13);
        var ghIssue = reader.IsDBNull(14) ? null : reader.GetString(14);
        var appliedSha = reader.IsDBNull(15) ? null : reader.GetString(15);

        return new DiagnosticRecord(
            id, fingerprint, firstSeen, lastSeen, occurrence,
            appName, environment, exceptionType, exceptionMessage, normalizedStack,
            sourceFile, sourceLine, diagnosis, resolution, ghIssue, appliedSha);
    }
}
