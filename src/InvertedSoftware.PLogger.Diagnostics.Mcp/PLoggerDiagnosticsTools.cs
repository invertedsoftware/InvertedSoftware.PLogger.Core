// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using System.ComponentModel;
using ModelContextProtocol.Server;

namespace InvertedSoftware.PLogger.Diagnostics.Mcp;

/// <summary>
/// MCP tools that expose <see cref="IDiagnosticStore"/> over the Model
/// Context Protocol so Claude Code, Copilot, and other dev-time agents
/// can pull production diagnostics into the editor.
/// </summary>
/// <remarks>
/// Method names use snake_case by MCP protocol convention.
/// </remarks>
[McpServerToolType]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores",
    Justification = "MCP tool names use snake_case by protocol convention.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Tools must be instance members so DI-injected dependencies are reachable.")]
public sealed class PLoggerDiagnosticsTools
{
    private readonly IDiagnosticStore _store;

    public PLoggerDiagnosticsTools(IDiagnosticStore store)
    {
        _store = store;
    }

    [McpServerTool, Description("Returns the diagnostic record for a known fingerprint (hex). Includes the LLM hypothesis and any proposed fix.")]
    public async ValueTask<DiagnosticRecord?> get_diagnosis(
        [Description("The 32-character hex fingerprint returned by /throw/fingerprint or list_unresolved.")] string fingerprintHex,
        CancellationToken cancellationToken = default)
    {
        var bytes = Convert.FromHexString(fingerprintHex);
        return await _store.GetByFingerprintAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool, Description("Marks a diagnostic as resolved by a specific commit. Closes the feedback loop so future occurrences are deprioritised.")]
    public async ValueTask<string> mark_fix_applied(
        [Description("Hex fingerprint identifying the diagnostic.")] string fingerprintHex,
        [Description("Commit SHA where the fix landed.")] string commitSha,
        CancellationToken cancellationToken = default)
    {
        var bytes = Convert.FromHexString(fingerprintHex);
        await _store.MarkFixAppliedAsync(bytes, commitSha, cancellationToken).ConfigureAwait(false);
        return "applied";
    }

    [McpServerTool, Description("Server health probe — confirms the MCP endpoint is alive and lists which tools are available.")]
    public IReadOnlyList<string> list_tools()
        => new[]
        {
            "get_diagnosis",
            "mark_fix_applied",
            "list_tools",
            // The remaining tools are stubbed in this preview — full surface
            // (get_recent_diagnostics, find_similar, list_unresolved,
            // get_recent_logs) lands in Phase 5.x once the store gains the
            // corresponding query methods.
        };
}
