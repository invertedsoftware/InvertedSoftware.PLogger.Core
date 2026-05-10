// Copyright (c) Inverted Software. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Core;

/// <summary>
/// Bound, hot-reloadable options for PLogger. Every key in the original
/// <c>PLoggerSettings</c> contract round-trips through this type via
/// <see cref="IOptionsMonitor{TOptions}"/>.
/// </summary>
public sealed class PLoggerOptions
{
    /// <summary>Configuration section path used when binding from <see cref="IConfiguration"/>.</summary>
    public const string SectionName = "Logging:PLogger";

    /// <summary>Sink type — <c>"file"</c> or anything else (defaults to SQL).</summary>
    public string PLogType { get; set; } = "file";

    public bool PLogEnabled { get; set; } = true;

    public string BaseNameFile { get; set; } = "plogger.txt";

    public int PLogFileMaxSizeKB { get; set; } = 1024;

    public string PLogFileMessageTemplate { get; set; } = "{0}";

    public string StringConnection { get; set; } = "ErrorsConnection";

    public string StoredProcedureName { get; set; } = "AddError";

    public string MessageParameterName { get; set; } = "@ErrorName";

    public int PLogDeleteFilesOlderThanDays { get; set; }

    public long PLogStopLoggingIfSpaceSmallerThanMB { get; set; }

    /// <summary>Optional category/level filter. Not bindable from configuration nor serializable.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Func<string, LogLevel, bool>? Filter { get; set; }

    /// <summary>
    /// Capacity of the in-memory channel between the logging hot path and the
    /// background drain. Default is 8192. <see cref="System.Threading.Channels.BoundedChannelFullMode.DropOldest"/>
    /// is the policy when full.
    /// </summary>
    public int ChannelCapacity { get; set; } = 8192;

    /// <summary>
    /// Maximum time the drain service will wait for the channel to flush on shutdown.
    /// </summary>
    public TimeSpan ShutdownDrainTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Capacity of the secondary channel that fans events out to the
    /// diagnostic pipeline (Phase 2+). Independent from the file/SQL
    /// drain channel so heavy diagnostic work does not back-pressure
    /// the primary log path.
    /// </summary>
    public int DiagnosticChannelCapacity { get; set; } = 1024;

    /// <summary>
    /// When false (the default), no Diagnostics components are registered
    /// even if the Diagnostics package is referenced. Phase 2 made this
    /// strictly opt-in so 1.x consumers see zero behavioral change.
    /// </summary>
    public bool DiagnosticEnabled { get; set; }
}
