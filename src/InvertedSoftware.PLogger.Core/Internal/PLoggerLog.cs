// Copyright (c) Inverted Software. All rights reserved.

using Microsoft.Extensions.Logging;

namespace InvertedSoftware.PLogger.Core.Internal;

/// <summary>
/// Source-generated diagnostic logging the provider uses for its own
/// internal failures. Errors here must never reach user code — they are
/// emitted to whatever <see cref="ILogger"/> the host provided so the
/// outer log call still succeeds.
/// </summary>
internal static partial class PLoggerLog
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning,
        Message = "PLogger drain failed for one entry; dropping. Sink={SinkType}.")]
    public static partial void DrainEntryFailed(ILogger logger, string sinkType, Exception exception);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning,
        Message = "PLogger sink threw on shutdown; ignored. Sink={SinkType}.")]
    public static partial void SinkShutdownFailed(ILogger logger, string sinkType, Exception exception);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information,
        Message = "PLogger drain stopping; flushing {Pending} pending entries with {GraceMs}ms grace.")]
    public static partial void DrainStopping(ILogger logger, int pending, double graceMs);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information,
        Message = "PLogger drain stopped.")]
    public static partial void DrainStopped(ILogger logger);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Warning,
        Message = "PLogger old-file cleanup failed for {File}.")]
    public static partial void FileCleanupFailed(ILogger logger, string file, Exception exception);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Warning,
        Message = "PLogger could not initialize file sink at {Path}; further file logging is disabled.")]
    public static partial void FileSinkInitFailed(ILogger logger, string path, Exception exception);
}
