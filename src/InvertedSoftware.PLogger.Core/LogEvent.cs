// Copyright (c) Inverted Software. All rights reserved.

using Microsoft.Extensions.Logging;

namespace InvertedSoftware.PLogger.Core;

/// <summary>
/// The unit of work that travels across the channel between the
/// <see cref="ILogger"/> hot path and the drain service.
/// </summary>
/// <remarks>
/// All formatting and I/O happen on the drain side. The hot path allocates
/// exactly one of these per log call.
/// </remarks>
public sealed record LogEvent(
    DateTimeOffset TimestampUtc,
    LogLevel Level,
    string Category,
    EventId EventId,
    string Message,
    Exception? Exception,
    IReadOnlyList<KeyValuePair<string, object?>> Scope,
    string? TraceId,
    string? SpanId);
