// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

namespace InvertedSoftware.PLogger.Diagnostics;

/// <summary>
/// Hot-reloadable options for the diagnostic pipeline. Bound from
/// <c>Logging:PLogger:Diagnostics</c>.
/// </summary>
public sealed class PLoggerDiagnosticsOptions
{
    /// <summary>Configuration section path.</summary>
    public const string SectionName = "Logging:PLogger:Diagnostics";

    /// <summary>Master switch. False (default) means no diagnostic services run.</summary>
    public bool Enabled { get; set; }

    /// <summary>Logical name of the host application; flows to <c>plogger.Diagnostics.AppName</c>.</summary>
    public string AppName { get; set; } = "default";

    /// <summary>Environment label (Production / Staging / etc).</summary>
    public string Environment { get; set; } = "Production";

    /// <summary>
    /// Capacity of the secondary channel between the capture sink and the
    /// diagnostic pipeline. Lower = more aggressive dedup back-pressure
    /// when the pipeline can't keep up. Falls through to
    /// <see cref="Core.PLoggerOptions.DiagnosticChannelCapacity"/>.
    /// </summary>
    public int? ChannelCapacityOverride { get; set; }

    /// <summary>
    /// Maximum time the diagnostic pipeline will wait for the channel to
    /// flush on shutdown. Independent from the file/SQL drain timeout.
    /// </summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Minimum severity gate. Below this level, the capture sink doesn't push
    /// the event onto the diagnostic channel. Default Error (Phase 2 only
    /// captures errors and criticals).
    /// </summary>
    public Microsoft.Extensions.Logging.LogLevel MinSeverity { get; set; } = Microsoft.Extensions.Logging.LogLevel.Error;
}
