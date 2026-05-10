// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

namespace InvertedSoftware.PLogger.Diagnostics.SourceLink;

/// <summary>
/// Hot-reloadable Source Link resolver options. Bound from
/// <c>Logging:PLogger:Diagnostics:SourceLink</c>.
/// </summary>
public sealed class SourceLinkOptions
{
    public const string SectionName = "Logging:PLogger:Diagnostics:SourceLink";

    /// <summary>Master switch.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// GitHub PAT used for raw API fetches when the source file isn't
    /// available locally. Optional — public repos work without auth.
    /// </summary>
    public string? GitHubToken { get; set; }

    /// <summary>
    /// Namespace prefixes whose frames are eligible for resolution.
    /// Defense-in-depth — frames outside the allow-list are never even
    /// looked up, preventing accidental third-party source disclosure.
    /// </summary>
    public IList<string> AllowedNamespaces { get; set; } = new List<string>();

    /// <summary>
    /// Number of context lines (above + below) to include in
    /// <see cref="DiagnosticContext.Source"/>'s <c>CodeSpan</c>.
    /// </summary>
    public int ContextLines { get; set; } = 5;

    /// <summary>Per-resolve HTTP timeout for GitHub raw fetches.</summary>
    public TimeSpan FetchTimeout { get; set; } = TimeSpan.FromSeconds(3);
}
