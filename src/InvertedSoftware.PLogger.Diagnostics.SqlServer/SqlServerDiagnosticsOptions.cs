// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

namespace InvertedSoftware.PLogger.Diagnostics.SqlServer;

/// <summary>
/// SQL Server store-specific options. Bound from
/// <c>Logging:PLogger:Diagnostics:Storage</c>.
/// </summary>
public sealed class SqlServerDiagnosticsOptions
{
    public const string SectionName = "Logging:PLogger:Diagnostics:Storage";

    /// <summary>SQL Server connection string.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Whether the schema initializer runs on host start. Set to false in
    /// production after the first deploy if you prefer migrations under
    /// external control.
    /// </summary>
    public bool ApplySchemaOnStartup { get; set; } = true;

    /// <summary>
    /// Per-call SQL command timeout (seconds). Defaults to 5 — diagnostics
    /// are best-effort; we don't want a slow store backing up the pipeline.
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = 5;
}
