// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using System.Text.RegularExpressions;

namespace InvertedSoftware.PLogger.Diagnostics.Agents;

/// <summary>
/// Pure-logic validator. Checks that a <see cref="ProposedFix"/> looks
/// like a real unified diff and that it targets exactly the file the
/// hypothesis implicated. No LLM call.
/// </summary>
public sealed partial class ValidatorAgent : IAgent<(ProposedFix Fix, string ExpectedFilePath), ValidationResult>
{
    [GeneratedRegex(@"^---\s+a/(.+)$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex MinusHeaderRegex();

    [GeneratedRegex(@"^\+\+\+\s+b/(.+)$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex PlusHeaderRegex();

    [GeneratedRegex(@"^@@\s+-\d+(?:,\d+)?\s+\+\d+(?:,\d+)?\s+@@", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex HunkHeaderRegex();

    public ValueTask<ValidationResult?> RunAsync(
        (ProposedFix Fix, string ExpectedFilePath) input,
        CancellationToken cancellationToken)
        => ValueTask.FromResult<ValidationResult?>(Validate(input.Fix, input.ExpectedFilePath));

    /// <summary>Synchronous core for unit tests.</summary>
    public static ValidationResult Validate(ProposedFix fix, string expectedFilePath)
    {
        ArgumentNullException.ThrowIfNull(fix);
        ArgumentException.ThrowIfNullOrEmpty(expectedFilePath);

        if (string.IsNullOrWhiteSpace(fix.UnifiedDiff))
        {
            return new ValidationResult(false, "Empty unified diff.");
        }

        // Dedupe: a multi-hunk diff for one file legitimately repeats the
        // header pair in some emitter modes; we only care about distinct
        // files referenced.
        var minusFiles = ExtractAll(MinusHeaderRegex(), fix.UnifiedDiff)
            .Distinct(StringComparer.Ordinal).ToList();
        var plusFiles = ExtractAll(PlusHeaderRegex(), fix.UnifiedDiff)
            .Distinct(StringComparer.Ordinal).ToList();

        if (minusFiles.Count == 0 || plusFiles.Count == 0)
        {
            return new ValidationResult(false, "Diff is missing standard --- a/<path> and +++ b/<path> headers.");
        }

        if (!HunkHeaderRegex().IsMatch(fix.UnifiedDiff))
        {
            return new ValidationResult(false, "Diff is missing at least one @@ hunk header.");
        }

        // Single-file scope: each side must reference exactly one distinct file.
        if (minusFiles.Count > 1 || plusFiles.Count > 1)
        {
            return new ValidationResult(false, $"Diff touches multiple files (expected only {expectedFilePath}).");
        }

        if (!FilePathMatches(minusFiles[0], expectedFilePath) || !FilePathMatches(plusFiles[0], expectedFilePath))
        {
            return new ValidationResult(false, $"Diff targets {plusFiles[0]} but expected {expectedFilePath}.");
        }

        // ProposedFix.FilePath must agree with the diff headers.
        if (!FilePathMatches(fix.FilePath, plusFiles[0]))
        {
            return new ValidationResult(false, $"ProposedFix.FilePath ({fix.FilePath}) disagrees with diff +++ b/{plusFiles[0]}.");
        }

        return new ValidationResult(true, null);
    }

    private static List<string> ExtractAll(Regex regex, string input)
    {
        var results = new List<string>(2);
        foreach (var m in regex.Matches(input).Cast<Match>())
        {
            if (m.Groups.Count > 1)
            {
                // Multiline-mode '$' matches before '\n' but leaves '\r' inside the
                // captured group on Windows-formatted output. TrimEnd is narrow on
                // purpose — we don't want to strip leading whitespace that's part
                // of the path (rare but legal).
                results.Add(m.Groups[1].Value.TrimEnd('\r', ' ', '\t'));
            }
        }
        return results;
    }

    private static bool FilePathMatches(string a, string b)
    {
        // Diff headers are usually relative ("src/MyApp/Foo.cs") while
        // StackFrame paths are absolute ("C:\repo\src\MyApp\Foo.cs").
        // Suffix-match is the pragmatic cross-platform answer, but we
        // require the matched suffix to start at a path-separator
        // boundary (or full equality) — otherwise "oo.cs" would suffix-match
        // "Foo.cs" and let near-name collisions through.
        var na = a.Replace('\\', '/').TrimStart('.', '/');
        var nb = b.Replace('\\', '/').TrimStart('.', '/');
        if (na.Equals(nb, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return EndsWithBoundary(na, nb) || EndsWithBoundary(nb, na);

        static bool EndsWithBoundary(string longer, string shorter)
        {
            if (longer.Length <= shorter.Length)
            {
                return false;
            }
            if (!longer.EndsWith(shorter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            // Char before the matched suffix must be '/' for the match to be
            // path-segment-aligned.
            return longer[^(shorter.Length + 1)] == '/';
        }
    }
}
