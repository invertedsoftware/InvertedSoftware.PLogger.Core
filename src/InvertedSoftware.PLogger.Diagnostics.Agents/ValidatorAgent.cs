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

        var minusFiles = ExtractAll(MinusHeaderRegex(), fix.UnifiedDiff);
        var plusFiles = ExtractAll(PlusHeaderRegex(), fix.UnifiedDiff);

        if (minusFiles.Count == 0 || plusFiles.Count == 0)
        {
            return new ValidationResult(false, "Diff is missing standard --- a/<path> and +++ b/<path> headers.");
        }

        if (!HunkHeaderRegex().IsMatch(fix.UnifiedDiff))
        {
            return new ValidationResult(false, "Diff is missing at least one @@ hunk header.");
        }

        // Single-file scope: every header must reference the same file.
        if (minusFiles.Count + plusFiles.Count > 2)
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
                results.Add(m.Groups[1].Value.Trim());
            }
        }
        return results;
    }

    private static bool FilePathMatches(string a, string b)
    {
        // Normalise path separators and compare suffix-based — diff headers
        // are usually relative ("src/MyApp/Foo.cs") while StackFrame paths
        // are absolute ("C:\repo\src\MyApp\Foo.cs"). Suffix match is the
        // pragmatic cross-platform answer.
        var na = a.Replace('\\', '/').TrimStart('.', '/');
        var nb = b.Replace('\\', '/').TrimStart('.', '/');
        return na.EndsWith(nb, StringComparison.OrdinalIgnoreCase)
            || nb.EndsWith(na, StringComparison.OrdinalIgnoreCase);
    }
}
