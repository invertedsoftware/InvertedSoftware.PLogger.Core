// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using InvertedSoftware.PLogger.Core;
using Microsoft.Extensions.Logging;

namespace InvertedSoftware.PLogger.Diagnostics;

/// <summary>
/// 16-byte exception fingerprint plus the normalized stack the hash was computed over.
/// Stable across line-number, generic-arg, lambda-display-class, and async-state-machine
/// variation — see <see cref="Core.Fingerprinting.StackFingerprinter"/>.
/// </summary>
public sealed record ExceptionFingerprint(byte[] Hash, string NormalizedStack)
{
    /// <summary>Hex-string view of <see cref="Hash"/>; convenient for logs and MCP tool I/O.</summary>
    public string HashHex => Convert.ToHexString(Hash);
}

/// <summary>
/// Source-code excerpt resolved from a stack frame via PortablePDB + Source Link.
/// Phase 2 always populates this as <c>null</c>; Phase 3 fills it in.
/// </summary>
public sealed record SourceSnippet(
    string FilePath,
    int LineNumber,
    string CodeSpan,
    string? GitBlameAuthor,
    string? CommitSha);

/// <summary>
/// Aggregated context handed to every <see cref="IDiagnosticAgent"/>.
/// </summary>
public sealed record DiagnosticContext(
    LogEvent Event,
    ExceptionFingerprint Fingerprint,
    SourceSnippet? Source,
    IReadOnlyList<LogEvent> RecentTrail,
    IReadOnlyList<DiagnosticRecord> SimilarPriorErrors);

/// <summary>
/// What an <see cref="IDiagnosticAgent"/> produces. Phase 2's no-op agent
/// returns <c>null</c>. Phase 3+ fill in <see cref="Hypothesis"/> and
/// optionally <see cref="Fix"/>.
/// </summary>
public sealed record DiagnosisResult(
    RootCauseHypothesis Hypothesis,
    ProposedFix? Fix,
    double ConfidenceScore,
    string ModelVersion,
    TimeSpan Duration);

public sealed record RootCauseHypothesis(
    ErrorCategory Category,
    string Explanation,
    IReadOnlyList<string> ContributingFactors);

public sealed record ProposedFix(
    string FilePath,
    string UnifiedDiff,
    string Rationale,
    RiskLevel Risk,
    IReadOnlyList<string> RegressionConcerns);

/// <summary>
/// Persistent shape returned from <see cref="IDiagnosticStore"/>. Mirrors the
/// SQL Server <c>plogger.Diagnostics</c> table; the abstractions package is
/// provider-neutral, but the field names match for round-trip clarity.
/// </summary>
public sealed record DiagnosticRecord(
    long Id,
    byte[] Fingerprint,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc,
    long OccurrenceCount,
    string AppName,
    string Environment,
    string ExceptionType,
    string ExceptionMessage,
    string NormalizedStack,
    string? SourceFile,
    int? SourceLine,
    string? Diagnosis,
    ResolutionState ResolutionState,
    string? GitHubIssueUrl,
    string? AppliedCommitSha);

/// <summary>Categorical bucket assigned by the (Phase 4) triage agent.</summary>
public enum ErrorCategory
{
    Unknown = 0,
    Transient,
    Logic,
    Config,
    NullDeref,
    Concurrency,
    ExternalDependency,
    Data,
}

/// <summary>Risk level of a proposed code change. Notifiers gate auto-PR on Low.</summary>
public enum RiskLevel
{
    Low = 0,
    Medium,
    High,
    RequiresReview,
}

/// <summary>Where a diagnostic sits in its triage / fix / verify lifecycle.</summary>
public enum ResolutionState : byte
{
    New = 0,
    Triaged = 1,
    FixProposed = 2,
    Applied = 3,
    Verified = 4,
    Ignored = 5,
}
