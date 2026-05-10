// Copyright (c) Inverted Software. All rights reserved.

using System.IO.Hashing;
using System.Text;
using System.Text.RegularExpressions;

namespace InvertedSoftware.PLogger.Core.Fingerprinting;

/// <summary>
/// Computes a stable byte-level fingerprint for an exception by normalising
/// the stack trace (strip line numbers, IL offsets, generic args, lambda
/// display classes, async state-machine machinery) and hashing the result
/// with <see cref="XxHash3"/>.
/// </summary>
/// <remarks>
/// The fingerprint is the input to dedupe / similarity gates in Phase 2+.
/// In Phase 1 it is exposed only via tests and is part of the API surface
/// so downstream packages can take a hard reference now.
/// </remarks>
public static partial class StackFingerprinter
{
    private const string Delimiter = " || ";
    private const char Newline = '\n';

    [GeneratedRegex(@"\s+in\s+.+?:line\s+\d+", RegexOptions.CultureInvariant)]
    private static partial Regex LineNumberRegex();

    [GeneratedRegex(@"\[0x[0-9a-fA-F]+\]\s*", RegexOptions.CultureInvariant)]
    private static partial Regex IlOffsetRegex();

    [GeneratedRegex(@"`\d+\[[^\]]*\]", RegexOptions.CultureInvariant)]
    private static partial Regex GenericArityArgsRegex();

    [GeneratedRegex(@"<>c__DisplayClass\d+(_\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex DisplayClassRegex();

    // CoreCLR uses <Method>d__N for async state machines.
    // Mono's roslyn-equivalent compiler uses the same shape for modern code,
    // but historical Mono / older monodevelop emitted <Method>c__async\d+.
    [GeneratedRegex(@"<[^>]+>(?:d__|c__async)\d+", RegexOptions.CultureInvariant)]
    private static partial Regex AsyncStateMachineRegex();

    // CoreCLR closure-class flavours: <>c, <>c__N, <>c__N_M.
    // Mono additionally emits <>c__AnonStorey\d+ and <Method>c__Iterator\d+
    // for iterators / closures.
    [GeneratedRegex(@"<>c(?:__\d+(?:_\d+)?)?|<>c__AnonStorey\d+|<[^>]+>c__Iterator\d+",
        RegexOptions.CultureInvariant)]
    private static partial Regex ClosureClassRegex();

    [GeneratedRegex(@"\.MoveNext\b", RegexOptions.CultureInvariant)]
    private static partial Regex MoveNextRegex();

    [GeneratedRegex(@"\s+at\s+System\.Runtime\.ExceptionServices\..*", RegexOptions.CultureInvariant)]
    private static partial Regex ExceptionServicesFrameRegex();

    /// <summary>
    /// Returns the normalized stack representation used as fingerprint input.
    /// Public so tests can assert on individual rules.
    /// </summary>
    public static string Normalize(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        var sb = new StringBuilder(512);
        var current = ex;
        var first = true;
        while (current is not null)
        {
            if (!first)
            {
                sb.Append(Delimiter);
            }
            first = false;

            sb.Append(current.GetType().FullName ?? current.GetType().Name);
            var stack = current.StackTrace;
            if (!string.IsNullOrEmpty(stack))
            {
                sb.Append(Newline);
                sb.Append(NormalizeStackTrace(stack));
            }

            current = current.InnerException;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Computes a stable 16-byte xxHash3 fingerprint over the result of
    /// <see cref="Normalize"/>.
    /// </summary>
    public static byte[] Compute(Exception ex)
    {
        var normalized = Normalize(ex);
        var bytes = Encoding.UTF8.GetBytes(normalized);

        // XxHash3 produces a 64-bit digest; broaden to 128-bit by mixing in
        // a second hash with a non-zero seed so the result is 16 bytes —
        // enough entropy for a fingerprint store keyed on BINARY(16).
        var lo = XxHash3.HashToUInt64(bytes);
        var hi = XxHash3.HashToUInt64(bytes, seed: unchecked((long)0x9E3779B97F4A7C15UL));

        var output = new byte[16];
        BitConverter.TryWriteBytes(output.AsSpan(0, 8), lo);
        BitConverter.TryWriteBytes(output.AsSpan(8, 8), hi);
        return output;
    }

    private static string NormalizeStackTrace(string stack)
    {
        var working = stack.Replace("\r\n", "\n", StringComparison.Ordinal);

        working = ExceptionServicesFrameRegex().Replace(working, string.Empty);
        working = LineNumberRegex().Replace(working, string.Empty);
        working = IlOffsetRegex().Replace(working, string.Empty);
        working = AsyncStateMachineRegex().Replace(working, "<async>");
        working = DisplayClassRegex().Replace(working, "<display>");
        working = ClosureClassRegex().Replace(working, "<display>");
        working = MoveNextRegex().Replace(working, ".<async>");
        working = GenericArityArgsRegex().Replace(working, GenericArityReplacer);

        var sb = new StringBuilder(working.Length);
        foreach (var rawLine in working.Split('\n'))
        {
            var trimmed = rawLine.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }
            if (sb.Length > 0)
            {
                sb.Append(Newline);
            }
            sb.Append(trimmed);
        }
        return sb.ToString();
    }

    private static string GenericArityReplacer(Match m)
    {
        // `1[T] → `1
        var raw = m.Value;
        var bracket = raw.IndexOf('[', StringComparison.Ordinal);
        return bracket < 0 ? raw : raw[..bracket];
    }
}
