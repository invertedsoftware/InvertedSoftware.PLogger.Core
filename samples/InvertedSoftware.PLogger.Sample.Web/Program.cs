// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using InvertedSoftware.PLogger.Core.Fingerprinting;
using InvertedSoftware.PLogger.Diagnostics;
using InvertedSoftware.PLogger.Sample.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(static o =>
{
    o.SingleLine = true;
    o.IncludeScopes = true;
});
builder.Logging.AddPLogger();

// Diagnostics + SQL store light up only when the master switch is on AND a
// connection string is configured. Default behavior matches Phase 1 exactly.
var diag = builder.Logging.AddPLoggerDiagnostics();
if (!string.IsNullOrEmpty(builder.Configuration["Logging:PLogger:Diagnostics:Storage:ConnectionString"]))
{
    diag.AddSqlServerStore();
}

var app = builder.Build();

app.MapGet("/", static () => Results.Ok(new
{
    name = "InvertedSoftware.PLogger.Sample.Web",
    docs = "/health, /log, /throw, /diagnose, /diagnostics/{fingerprintHex}",
}));

app.MapGet("/health", static () => Results.Ok(new { status = "ok" }));

app.MapGet("/log", static (ILoggerFactory factory) =>
{
    var logger = factory.CreateLogger("Sample");
    SampleLog.Logged(logger, DateTimeOffset.UtcNow);
    return Results.Ok("logged");
});

app.MapGet("/throw", static (ILoggerFactory factory) =>
{
    var logger = factory.CreateLogger("Sample");
    try
    {
        ThrowOnDemand();
        return Results.Ok("unreachable");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "boom from /throw");
        return Results.StatusCode(StatusCodes.Status500InternalServerError);
    }
});

// Reads the diagnostic store back. Useful for confirming dedup end-to-end:
//   curl /throw   (50 times)
//   curl /diagnostics/{fingerprintHex}
// Should show OccurrenceCount=50.
app.MapGet("/diagnostics/{fingerprintHex}",
    static async (string fingerprintHex, IDiagnosticStore? store, CancellationToken ct) =>
    {
        if (store is null)
        {
            return Results.Problem("No IDiagnosticStore registered. Set Logging:PLogger:Diagnostics:Storage:ConnectionString.");
        }
        var fp = Convert.FromHexString(fingerprintHex);
        var record = await store.GetByFingerprintAsync(fp, ct);
        return record is null ? Results.NotFound() : Results.Ok(record);
    });

// Helper: returns the canonical fingerprint hex for the /throw exception so
// you don't have to compute it by hand.
app.MapGet("/throw/fingerprint", static () =>
{
    try { ThrowOnDemand(); }
    catch (Exception ex)
    {
        return Results.Ok(new
        {
            fingerprintHex = Convert.ToHexString(StackFingerprinter.Compute(ex)),
        });
    }
    return Results.Problem("unreachable");
});

// Synchronous diagnose: throws, runs the configured IDiagnosticAgent inline,
// and returns the resulting hypothesis + proposed fix. Demonstrates the
// AI-driven flow without waiting for the channel/drain to catch up.
app.MapGet("/diagnose",
    static async (IDiagnosticEnricher enricher, IDiagnosticAgent agent, CancellationToken ct) =>
    {
        try { ThrowOnDemand(); }
        catch (Exception ex)
        {
            var evt = new InvertedSoftware.PLogger.Core.LogEvent(
                DateTimeOffset.UtcNow,
                Microsoft.Extensions.Logging.LogLevel.Error,
                "Sample.Diagnose",
                new EventId(1),
                "synthetic for diagnosis",
                ex,
                Array.Empty<KeyValuePair<string, object?>>(),
                System.Diagnostics.Activity.Current?.TraceId.ToString(),
                System.Diagnostics.Activity.Current?.SpanId.ToString());

            var ctx = await enricher.EnrichAsync(evt, ct);
            if (ctx is null)
            {
                return Results.Problem("Enricher returned null.");
            }
            var diagnosis = await agent.DiagnoseAsync(ctx, ct);
            return Results.Ok(new
            {
                fingerprintHex = ctx.Fingerprint.HashHex,
                diagnosis,
                source = ctx.Source,
            });
        }
        return Results.Problem("unreachable");
    });

app.Run();

static void ThrowOnDemand() =>
    throw new InvalidOperationException("Sample exception triggered by /throw endpoint.");

public partial class Program;
