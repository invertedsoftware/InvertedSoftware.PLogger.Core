// Copyright (c) Inverted Software. All rights reserved.

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

var app = builder.Build();

app.MapGet("/", static () => Results.Ok(new
{
    name = "InvertedSoftware.PLogger.Sample.Web",
    docs = "/health, /log, /throw",
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

app.Run();

static void ThrowOnDemand() =>
    throw new InvalidOperationException("Sample exception triggered by /throw endpoint.");

public partial class Program;
