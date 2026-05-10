// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using Microsoft.Extensions.Logging;

namespace InvertedSoftware.PLogger.Diagnostics.Internal;

/// <summary>
/// Source-generated diagnostic logging the package uses to report internal
/// failures to the host's <see cref="ILogger"/>. Never recurses into PLogger.
/// </summary>
internal static partial class DiagnosticsLog
{
    [LoggerMessage(EventId = 2001, Level = LogLevel.Warning,
        Message = "Diagnostic enrichment failed for category {Category}; pipeline skipped this event.")]
    public static partial void EnrichmentFailed(ILogger logger, string category, Exception exception);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning,
        Message = "Diagnostic agent failed for fingerprint {FingerprintHex}; persisting without diagnosis.")]
    public static partial void AgentFailed(ILogger logger, string fingerprintHex, Exception exception);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Warning,
        Message = "Diagnostic store upsert failed for fingerprint {FingerprintHex}; entry dropped.")]
    public static partial void StoreUpsertFailed(ILogger logger, string fingerprintHex, Exception exception);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Information,
        Message = "Diagnostic pipeline stopping; flushing {Pending} pending entries with {GraceMs}ms grace.")]
    public static partial void PipelineStopping(ILogger logger, int pending, double graceMs);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Information,
        Message = "Diagnostic pipeline stopped.")]
    public static partial void PipelineStopped(ILogger logger);

    [LoggerMessage(EventId = 2006, Level = LogLevel.Warning,
        Message = "Diagnostic notifier {Notifier} failed; ignored.")]
    public static partial void NotifierFailed(ILogger logger, string notifier, Exception exception);

    [LoggerMessage(EventId = 2007, Level = LogLevel.Warning,
        Message = "Diagnostic pipeline drained one entry but the inner ProcessAsync threw; entry skipped.")]
    public static partial void PipelineDrainEntryFailed(ILogger logger, Exception exception);
}
