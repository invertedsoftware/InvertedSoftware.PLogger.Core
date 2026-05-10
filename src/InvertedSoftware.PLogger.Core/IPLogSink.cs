// Copyright (c) Inverted Software. All rights reserved.

namespace InvertedSoftware.PLogger.Core;

/// <summary>
/// A target the drain service writes <see cref="LogEvent"/>s to. One instance
/// is selected at registration based on <see cref="PLoggerOptions.PLogType"/>
/// and lives for the lifetime of the host.
/// </summary>
/// <remarks>
/// Implementations must be safe to call from a single reader and must never
/// throw; failures are the sink's responsibility to swallow + self-report so
/// that the drain loop continues. See <c>PLoggerDrainService</c> for the
/// invariant.
/// </remarks>
public interface IPLogSink : IAsyncDisposable
{
    ValueTask WriteAsync(LogEvent evt, CancellationToken cancellationToken);
}
