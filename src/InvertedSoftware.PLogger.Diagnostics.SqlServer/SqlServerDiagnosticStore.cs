// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using System.Data;
using System.Text.Json;
using InvertedSoftware.PLogger.Diagnostics.Internal;
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
                // The column is documented as JSON. Default record ToString
                // is NOT JSON, so we serialize via the source-gen context.
                Value = diagnosis is null
                    ? (object)DBNull.Value
                    : JsonSerializer.Serialize(diagnosis, DiagnosticsJsonContext.Default.DiagnosisResult),
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

    public async ValueTask<IReadOnlyList<DiagnosticRecord>> FindSimilarAsync(
        ReadOnlyMemory<float> embedding,
        double threshold,
        int limit,
        CancellationToken cancellationToken)
    {
        if (embedding.IsEmpty)
        {
            return EmptySimilar;
        }

        var sqlOpts = _sqlOptions.CurrentValue;
        var diagOpts = _diagOptions.CurrentValue;

        // SQL Server 2025 native VECTOR_DISTANCE; on 2022 (used by the
        // dev-box Testcontainers image) this errors out — caller catches.
        // The pipeline tolerates a failing FindSimilarAsync as a no-hit.
        const string sql = @"
DECLARE @Probe VECTOR(1536) = JSON_ARRAY_TO_VECTOR(@Vector, 1536);

SELECT TOP(@Limit)
       Id, Fingerprint, FirstSeenUtc, LastSeenUtc, OccurrenceCount,
       AppName, Environment, ExceptionType, ExceptionMessage, NormalizedStack,
       SourceFile, SourceLine, Diagnosis, ResolutionState, GitHubIssueUrl, AppliedCommitSha,
       VECTOR_DISTANCE('cosine', DiagnosisVector, @Probe) AS Distance
  FROM plogger.Diagnostics
 WHERE AppName = @AppName AND Environment = @Environment AND DiagnosisVector IS NOT NULL
   AND VECTOR_DISTANCE('cosine', DiagnosisVector, @Probe) <= (1 - @Threshold)
 ORDER BY Distance ASC;
";
        await using var connection = new SqlConnection(sqlOpts.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = new SqlCommand(sql, connection);
        cmd.CommandTimeout = sqlOpts.CommandTimeoutSeconds;
        cmd.Parameters.Add(new SqlParameter("@Limit", SqlDbType.Int) { Value = limit });
        cmd.Parameters.Add(new SqlParameter("@AppName", SqlDbType.NVarChar, 200) { Value = diagOpts.AppName });
        cmd.Parameters.Add(new SqlParameter("@Environment", SqlDbType.NVarChar, 50) { Value = diagOpts.Environment });
        cmd.Parameters.Add(new SqlParameter("@Threshold", SqlDbType.Float) { Value = threshold });
        cmd.Parameters.Add(new SqlParameter("@Vector", SqlDbType.NVarChar, -1)
        {
            Value = SerializeVector(embedding.Span),
        });

        var results = new List<DiagnosticRecord>(limit);
        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(MapRecord(reader));
            }
        }
        catch (SqlException)
        {
            // VECTOR_DISTANCE not available (SQL < 2025) or vector column
            // empty. Treat as no-hit; the pipeline will run the agent.
            return EmptySimilar;
        }
        return results;
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

    /// <summary>
    /// AOT-safe JSON encoding of a float vector — SQL Server's
    /// CAST('[0.1,0.2,…]' AS VECTOR(N)) accepts the JSON-array shape.
    /// </summary>
    private static string SerializeVector(ReadOnlySpan<float> vector)
    {
        var sb = new System.Text.StringBuilder(vector.Length * 12);
        sb.Append('[');
        for (var i = 0; i < vector.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }
            sb.Append(vector[i].ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }
        sb.Append(']');
        return sb.ToString();
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
