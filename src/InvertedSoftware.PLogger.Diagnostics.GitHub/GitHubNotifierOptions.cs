// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

namespace InvertedSoftware.PLogger.Diagnostics.GitHub;

/// <summary>
/// GitHub notifier options. Bound from
/// <c>Logging:PLogger:Diagnostics:Notifiers:GitHub</c>.
/// </summary>
public sealed class GitHubNotifierOptions
{
    public const string SectionName = "Logging:PLogger:Diagnostics:Notifiers:GitHub";

    /// <summary>Master switch.</summary>
    public bool Enabled { get; set; }

    /// <summary>Repository in <c>owner/repo</c> form.</summary>
    public string Repository { get; set; } = string.Empty;

    /// <summary>GitHub PAT with <c>issues:write</c> scope (and <c>pull_requests:write</c> for the PR notifier).</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Optional labels applied to filed issues.</summary>
    public IList<string> Labels { get; set; } = new List<string> { "plogger", "auto-filed" };

    /// <summary>Minimum occurrence count required before an issue is filed.</summary>
    public long MinOccurrenceCount { get; set; } = 1;

    /// <summary>Per-call HTTP timeout.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);
}
