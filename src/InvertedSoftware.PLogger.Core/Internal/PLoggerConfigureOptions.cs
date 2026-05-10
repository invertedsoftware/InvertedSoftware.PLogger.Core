// Copyright (c) Inverted Software. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Core.Internal;

/// <summary>
/// AOT-safe binding of the <c>Logging:PLogger</c> configuration section to
/// <see cref="PLoggerOptions"/>. Each known key is read explicitly — no
/// reflection, no <c>ConfigurationBinder.Bind</c>.
/// </summary>
internal sealed class PLoggerConfigureOptions : IConfigureOptions<PLoggerOptions>
{
    private readonly IConfiguration _section;

    public PLoggerConfigureOptions(ILoggerProviderConfiguration<PLoggerProvider> providerConfig)
    {
        _section = providerConfig.Configuration;
    }

    public void Configure(PLoggerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Apply(_section, options);
    }

    public static void Apply(IConfiguration section, PLoggerOptions options)
    {
        if (TryReadString(section, "PLogType", out var pType))
        {
            options.PLogType = pType;
        }

        if (TryReadBool(section, "PLogEnabled", out var enabled))
        {
            options.PLogEnabled = enabled;
        }

        if (TryReadString(section, "BaseNameFile", out var baseFile))
        {
            options.BaseNameFile = baseFile;
        }

        if (TryReadInt(section, "PLogFileMaxSizeKB", out var maxKb))
        {
            options.PLogFileMaxSizeKB = maxKb;
        }

        if (TryReadString(section, "PLogFileMessageTemplate", out var template))
        {
            options.PLogFileMessageTemplate = template;
        }

        if (TryReadString(section, "StringConnection", out var conn))
        {
            options.StringConnection = conn;
        }

        if (TryReadString(section, "StoredProcedureName", out var sproc))
        {
            options.StoredProcedureName = sproc;
        }

        if (TryReadString(section, "MessageParameterName", out var param))
        {
            options.MessageParameterName = param;
        }

        if (TryReadInt(section, "PLogDeleteFilesOlderThanDays", out var deleteDays))
        {
            options.PLogDeleteFilesOlderThanDays = deleteDays;
        }

        if (TryReadLong(section, "PLogStopLoggingIfSpaceSmallerThanMB", out var spaceMb))
        {
            options.PLogStopLoggingIfSpaceSmallerThanMB = spaceMb;
        }

        if (TryReadInt(section, "ChannelCapacity", out var capacity) && capacity > 0)
        {
            options.ChannelCapacity = capacity;
        }
    }

    private static bool TryReadString(IConfiguration section, string key, out string value)
    {
        var raw = section[key];
        if (string.IsNullOrEmpty(raw))
        {
            value = string.Empty;
            return false;
        }
        value = raw;
        return true;
    }

    private static bool TryReadBool(IConfiguration section, string key, out bool value) =>
        bool.TryParse(section[key], out value);

    private static bool TryReadInt(IConfiguration section, string key, out int value) =>
        int.TryParse(section[key], System.Globalization.CultureInfo.InvariantCulture, out value);

    private static bool TryReadLong(IConfiguration section, string key, out long value) =>
        long.TryParse(section[key], System.Globalization.CultureInfo.InvariantCulture, out value);
}
