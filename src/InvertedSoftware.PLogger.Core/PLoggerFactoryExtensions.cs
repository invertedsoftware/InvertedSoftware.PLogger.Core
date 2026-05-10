// Copyright (c) Inverted Software. All rights reserved.

using InvertedSoftware.PLogger.Core;
using InvertedSoftware.PLogger.Core.Internal;
using InvertedSoftware.PLogger.Core.Sinks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Options;

// The original extensions live under Microsoft.Extensions.Logging so v1
// callers don't need a using-directive change.
namespace Microsoft.Extensions.Logging;

/// <summary>
/// Drop-in <c>AddPLogger</c> overloads. Every signature from PLogger 1.x is
/// preserved; under the hood they all converge on the new options + channel
/// + drain pipeline.
/// </summary>
public static class PLoggerFactoryExtensions
{
    public static ILoggingBuilder AddPLogger(this ILoggingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        RegisterCoreServices(builder);
        return builder;
    }

    public static ILoggingBuilder AddPLogger(this ILoggingBuilder builder, PLoggerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(settings);

        RegisterCoreServices(builder);
        builder.Services.Configure<PLoggerOptions>(opts => CopyFromSettings(settings, opts));
        return builder;
    }

    public static ILoggingBuilder AddPLogger(this ILoggingBuilder builder, Action<PLoggerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        RegisterCoreServices(builder);
        builder.Services.Configure(configure);
        return builder;
    }

    public static ILoggerFactory AddPLogger(this ILoggerFactory factory, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(configuration);

        var settings = new PLoggerSettings(configuration);
        return AddPLogger(factory, settings);
    }

    public static ILoggerFactory AddPLogger(this ILoggerFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return AddPLogger(factory, LogLevel.Information);
    }

    public static ILoggerFactory AddPLogger(this ILoggerFactory factory, LogLevel minLevel)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return AddPLogger(factory, new PLoggerSettings { Filter = (_, level) => level >= minLevel });
    }

    public static ILoggerFactory AddPLogger(this ILoggerFactory factory, PLoggerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(settings);

        // Standalone factory path (no DI host). Build the smallest possible
        // self-hosted scaffold so the legacy code path still works.
        var standalone = StandalonePLoggerHost.Create(settings.ToOptions());
        factory.AddProvider(standalone.Provider);
        return factory;
    }

    private static void RegisterCoreServices(ILoggingBuilder builder)
    {
        builder.AddConfiguration();

        var services = builder.Services;
        services.TryAddSingleton<PLoggerChannel>(static sp =>
            new PLoggerChannel(sp.GetRequiredService<IOptions<PLoggerOptions>>().Value.ChannelCapacity));
        services.TryAddSingleton<IPLogSink>(SinkFactory);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, PLoggerProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, PLoggerDrainService>());

        // Hand-rolled binding (AOT-safe — no ConfigurationBinder.Bind reflection).
        // Hot reload still works because we register a change-token source that
        // listens to the Logging:PLogger configuration section.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<PLoggerOptions>, PLoggerConfigureOptions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IOptionsChangeTokenSource<PLoggerOptions>,
            LoggerProviderOptionsChangeTokenSource<PLoggerOptions, PLoggerProvider>>());
    }

    private static IPLogSink SinkFactory(IServiceProvider sp)
    {
        var options = sp.GetRequiredService<IOptionsMonitor<PLoggerOptions>>();
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var current = options.CurrentValue;

        return current.PLogType.Equals("file", StringComparison.OrdinalIgnoreCase)
            ? new RollingFileLogSink(options, loggerFactory.CreateLogger<RollingFileLogSink>())
            : new SqlLogSink(options, loggerFactory.CreateLogger<SqlLogSink>());
    }

    private static void CopyFromSettings(PLoggerSettings src, PLoggerOptions dst)
    {
        dst.PLogType = src.PLogType;
        dst.PLogEnabled = src.PLogEnabled;
        dst.BaseNameFile = src.BaseNameFile;
        dst.PLogFileMaxSizeKB = src.PLogFileMaxSizeKB;
        dst.PLogFileMessageTemplate = src.PLogFileMessageTemplate;
        dst.StringConnection = src.StringConnection;
        dst.StoredProcedureName = src.StoredProcedureName;
        dst.MessageParameterName = src.MessageParameterName;
        dst.PLogDeleteFilesOlderThanDays = src.PLogDeleteFilesOlderThanDays;
        dst.PLogStopLoggingIfSpaceSmallerThanMB = src.PLogStopLoggingIfSpaceSmallerThanMB;
        dst.Filter = src.Filter;
    }
}
