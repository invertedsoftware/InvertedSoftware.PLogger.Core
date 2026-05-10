// Copyright (c) Inverted Software. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace InvertedSoftware.PLogger.Core;

/// <summary>
/// Back-compat surface for v1 consumers. New code should bind
/// <see cref="PLoggerOptions"/> via <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/>
/// instead. Both types share the same field names, so configuration round-trips.
/// </summary>
public class PLoggerSettings
{
    public PLoggerSettings()
    {
        var defaults = new PLoggerOptions();
        PLogType = defaults.PLogType;
        PLogEnabled = defaults.PLogEnabled;
        BaseNameFile = defaults.BaseNameFile;
        PLogFileMaxSizeKB = defaults.PLogFileMaxSizeKB;
        PLogFileMessageTemplate = defaults.PLogFileMessageTemplate;
        StringConnection = defaults.StringConnection;
        StoredProcedureName = defaults.StoredProcedureName;
        MessageParameterName = defaults.MessageParameterName;
        PLogDeleteFilesOlderThanDays = defaults.PLogDeleteFilesOlderThanDays;
        PLogStopLoggingIfSpaceSmallerThanMB = defaults.PLogStopLoggingIfSpaceSmallerThanMB;
    }

    public PLoggerSettings(IConfiguration configuration)
        : this()
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(PLoggerOptions.SectionName);

        var pType = section["PLogType"];
        if (!string.IsNullOrEmpty(pType))
        {
            PLogType = pType;
        }

        if (bool.TryParse(section["PLogEnabled"], out var enabled))
        {
            PLogEnabled = enabled;
        }

        var baseFile = section["BaseNameFile"];
        if (!string.IsNullOrEmpty(baseFile))
        {
            BaseNameFile = baseFile;
        }

        if (int.TryParse(section["PLogFileMaxSizeKB"], out var maxKb))
        {
            PLogFileMaxSizeKB = maxKb;
        }

        var template = section["PLogFileMessageTemplate"];
        if (!string.IsNullOrEmpty(template))
        {
            PLogFileMessageTemplate = template;
        }

        var conn = section["StringConnection"];
        if (!string.IsNullOrEmpty(conn))
        {
            StringConnection = conn;
        }

        var sproc = section["StoredProcedureName"];
        if (!string.IsNullOrEmpty(sproc))
        {
            StoredProcedureName = sproc;
        }

        var param = section["MessageParameterName"];
        if (!string.IsNullOrEmpty(param))
        {
            MessageParameterName = param;
        }

        if (int.TryParse(section["PLogDeleteFilesOlderThanDays"], out var deleteDays))
        {
            PLogDeleteFilesOlderThanDays = deleteDays;
        }

        if (long.TryParse(section["PLogStopLoggingIfSpaceSmallerThanMB"], out var spaceMb))
        {
            PLogStopLoggingIfSpaceSmallerThanMB = spaceMb;
        }
    }

    public string PLogType { get; set; }
    public bool PLogEnabled { get; set; }
    public string BaseNameFile { get; set; }
    public int PLogFileMaxSizeKB { get; set; }
    public string PLogFileMessageTemplate { get; set; }
    public Func<string, LogLevel, bool>? Filter { get; set; }
    public string StringConnection { get; set; }
    public string StoredProcedureName { get; set; }
    public string MessageParameterName { get; set; }
    public int PLogDeleteFilesOlderThanDays { get; set; }
    public long PLogStopLoggingIfSpaceSmallerThanMB { get; set; }

    internal PLoggerOptions ToOptions() => new()
    {
        PLogType = PLogType,
        PLogEnabled = PLogEnabled,
        BaseNameFile = BaseNameFile,
        PLogFileMaxSizeKB = PLogFileMaxSizeKB,
        PLogFileMessageTemplate = PLogFileMessageTemplate,
        StringConnection = StringConnection,
        StoredProcedureName = StoredProcedureName,
        MessageParameterName = MessageParameterName,
        PLogDeleteFilesOlderThanDays = PLogDeleteFilesOlderThanDays,
        PLogStopLoggingIfSpaceSmallerThanMB = PLogStopLoggingIfSpaceSmallerThanMB,
        Filter = Filter,
    };
}
