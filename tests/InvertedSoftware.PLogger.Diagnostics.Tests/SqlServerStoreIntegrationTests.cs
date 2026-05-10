// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using InvertedSoftware.PLogger.Core;
using InvertedSoftware.PLogger.Diagnostics;
using InvertedSoftware.PLogger.Diagnostics.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.MsSql;

namespace InvertedSoftware.PLogger.Diagnostics.Tests;

/// <summary>
/// End-to-end against a real SQL Server 2025 instance via Testcontainers.
/// Skipped if the Docker daemon isn't reachable (CI without Docker, dev box
/// without Docker Desktop, etc.).
/// </summary>
[Collection("SqlServerIntegration")]
public class SqlServerStoreIntegrationTests : IAsyncLifetime
{
    // Use SQL 2022 image as the closest publicly-available proxy. The 2025
    // VECTOR(N) column is reserved as NULL in the schema; the Phase 2 store
    // doesn't write to it, so 2022 is enough to validate the dedup contract.
    // Phase 3 will switch this to the SQL 2025 preview image.
    private MsSqlContainer? _container;
    private bool _started;

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new MsSqlBuilder()
                .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
                .Build();
            await _container.StartAsync();
            _started = true;
        }
        catch
        {
            _started = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_started && _container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    [Fact]
    public async Task Schema_AppliesIdempotently_AndUpsertDeduplicates()
    {
        Assert.SkipUnless(_started, "Docker daemon not reachable; SQL Server integration test skipped.");

        var conn = _container!.GetConnectionString();
        var sqlOpts = new SqlServerDiagnosticsOptions { ConnectionString = conn };

        // Apply once — should create both tables.
        await SqlServerSchemaInitializer.ApplySchemaAsync(sqlOpts, CancellationToken.None);
        // Apply again — should be a no-op (idempotency check).
        await SqlServerSchemaInitializer.ApplySchemaAsync(sqlOpts, CancellationToken.None);

        var store = new SqlServerDiagnosticStore(
            new TestMonitor<SqlServerDiagnosticsOptions>(sqlOpts),
            new TestMonitor<PLoggerDiagnosticsOptions>(new PLoggerDiagnosticsOptions
            {
                Enabled = true,
                AppName = "integration-test",
                Environment = "Test",
            }),
            NullLogger<SqlServerDiagnosticStore>.Instance);

        var enricher = new DefaultDiagnosticEnricher();

        // Same call site → identical fingerprint × 50.
        for (var i = 0; i < 50; i++)
        {
            var evt = MakeEvent(SyntheticException.Create());
            var ctx = await enricher.EnrichAsync(evt, CancellationToken.None);
            Assert.NotNull(ctx);
            await store.UpsertAsync(ctx, diagnosis: null, CancellationToken.None);
        }

        // Verify exactly 1 Diagnostics row + 50 occurrences via direct SQL.
        await using var connection = new SqlConnection(conn);
        await connection.OpenAsync();

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*), MAX(OccurrenceCount) FROM plogger.Diagnostics WHERE AppName='integration-test' AND Environment='Test';";
            await using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1, reader.GetInt32(0));
            Assert.Equal(50L, reader.GetInt64(1));
        }

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM plogger.DiagnosticOccurrences;";
            var count = (int)(await cmd.ExecuteScalarAsync())!;
            Assert.Equal(50, count);
        }
    }

    private static LogEvent MakeEvent(Exception ex) => new(
        DateTimeOffset.UtcNow, LogLevel.Error,
        "integration-cat", new EventId(1), "boom", ex,
        Array.Empty<KeyValuePair<string, object?>>(), null, null);

    private sealed class TestMonitor<T> : IOptionsMonitor<T> where T : class
    {
        public TestMonitor(T value) { CurrentValue = value; }
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
