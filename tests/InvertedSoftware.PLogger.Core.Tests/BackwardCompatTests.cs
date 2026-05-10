// Copyright (c) Inverted Software. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Core.Tests;

public class BackwardCompatTests
{
    [Fact]
    public void LegacyPLoggerSettings_BindsAllKnownKeys_FromOriginalAppSettings()
    {
        var config = LoadLegacyConfig();
        var settings = new PLoggerSettings(config);

        Assert.Equal("file", settings.PLogType);
        Assert.True(settings.PLogEnabled);
        Assert.Equal("plogger.txt", settings.BaseNameFile);
        Assert.Equal(1024, settings.PLogFileMaxSizeKB);
        Assert.Equal("{0}", settings.PLogFileMessageTemplate);
        Assert.Equal("StringConnection", settings.StringConnection);
        Assert.Equal("AddError", settings.StoredProcedureName);
        Assert.Equal("@ErrorName", settings.MessageParameterName);
        Assert.Equal(0, settings.PLogDeleteFilesOlderThanDays);
        Assert.Equal(0, settings.PLogStopLoggingIfSpaceSmallerThanMB);
    }

    [Fact]
    public void HostedAddPLogger_ProducesProvider_AndOptionsRoundTripFromConfig()
    {
        var config = LoadLegacyConfig();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, b) =>
            {
                b.Sources.Clear();
                b.AddConfiguration(config);
            })
            .ConfigureLogging((_, log) =>
            {
                log.ClearProviders();
                log.AddPLogger();
            })
            .Build();

        var monitor = host.Services.GetRequiredService<IOptionsMonitor<PLoggerOptions>>();
        var opts = monitor.CurrentValue;

        Assert.Equal("file", opts.PLogType);
        Assert.True(opts.PLogEnabled);
        Assert.Equal("plogger.txt", opts.BaseNameFile);
        Assert.Equal(1024, opts.PLogFileMaxSizeKB);
        Assert.Equal("{0}", opts.PLogFileMessageTemplate);
        Assert.Equal("StringConnection", opts.StringConnection);
        Assert.Equal("AddError", opts.StoredProcedureName);
        Assert.Equal("@ErrorName", opts.MessageParameterName);
    }

    [Fact]
    public void V1Signature_LoggerFactory_AddPLogger_Configuration_StillCompiles()
    {
        // Brief: "loggerFactory.AddPLogger(Configuration)" must keep working.
        var config = LoadLegacyConfig();
        using var factory = LoggerFactory.Create(static b => b.SetMinimumLevel(LogLevel.Information));

        // The exact v1 call site:
        var returned = factory.AddPLogger(config);

        Assert.NotNull(returned);
        var logger = returned.CreateLogger("compat");
        Assert.NotNull(logger);
        // Shouldn't throw on actual log call.
        logger.LogInformation("smoke test");
    }

    private static IConfigurationRoot LoadLegacyConfig()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "appsettings.legacy.json");
        Assert.True(File.Exists(path), $"expected fixture at {path}");
        return new ConfigurationBuilder().AddJsonFile(path, optional: false).Build();
    }
}
