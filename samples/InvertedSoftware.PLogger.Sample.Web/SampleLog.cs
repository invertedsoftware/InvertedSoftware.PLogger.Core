// Copyright (c) Inverted Software. All rights reserved.

using Microsoft.Extensions.Logging;

namespace InvertedSoftware.PLogger.Sample.Web;

internal static partial class SampleLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Hello from /log at {Timestamp}.")]
    public static partial void Logged(ILogger logger, DateTimeOffset timestamp);
}
