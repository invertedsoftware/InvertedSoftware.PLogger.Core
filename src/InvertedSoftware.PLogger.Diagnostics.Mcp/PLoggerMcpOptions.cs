// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

namespace InvertedSoftware.PLogger.Diagnostics.Mcp;

/// <summary>
/// MCP server configuration. Bound from
/// <c>Logging:PLogger:Diagnostics:Mcp</c>.
/// </summary>
public sealed class PLoggerMcpOptions
{
    public const string SectionName = "Logging:PLogger:Diagnostics:Mcp";

    /// <summary>Master switch — defaults to off.</summary>
    public bool Enabled { get; set; }

    /// <summary>Default route prefix when <c>MapPLoggerMcp()</c> is called without an explicit one.</summary>
    public string RoutePrefix { get; set; } = "/mcp/diagnostics";

    /// <summary>
    /// Required bearer token for incoming MCP requests. Empty in dev means
    /// no auth; production deployments MUST set this.
    /// </summary>
    public string? BearerToken { get; set; }
}
