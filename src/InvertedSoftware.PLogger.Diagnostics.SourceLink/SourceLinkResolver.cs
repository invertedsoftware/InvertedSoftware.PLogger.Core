// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using System.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Diagnostics.SourceLink;

/// <summary>
/// Phase-3 Source Link resolver. Walks the exception's frames in order
/// and returns the first frame whose source is reachable locally, gated
/// by <see cref="SourceLinkOptions.AllowedNamespaces"/>. Returns null if
/// nothing matches.
/// </summary>
/// <remarks>
/// Local-only in this preview: relies on <see cref="StackFrame.GetFileName"/>
/// + <see cref="File.Exists(string)"/>. Phase 3.x adds the PortablePDB
/// Source Link metadata fallback for cases where the build machine and
/// runtime machine differ. The interface is unchanged either way.
/// </remarks>
public sealed class SourceLinkResolver : ISourceLinkResolver
{
    private readonly IOptionsMonitor<SourceLinkOptions> _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SourceLinkResolver> _logger;

    public SourceLinkResolver(
        IOptionsMonitor<SourceLinkOptions> options,
        IMemoryCache cache,
        ILogger<SourceLinkResolver> logger)
    {
        _options = options;
        _cache = cache;
        _logger = logger;
    }

    public ValueTask<SourceSnippet?> ResolveAsync(Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var opts = _options.CurrentValue;
        if (!opts.Enabled)
        {
            return ValueTask.FromResult<SourceSnippet?>(null);
        }

        var allowed = opts.AllowedNamespaces;
        var trace = new StackTrace(exception, fNeedFileInfo: true);

        for (var i = 0; i < trace.FrameCount; i++)
        {
            var frame = trace.GetFrame(i);
            if (frame is null)
            {
                continue;
            }

            var declaringNamespace = ResolveNamespace(frame);
            if (!IsNamespaceAllowed(declaringNamespace, allowed))
            {
                continue;
            }

            var filePath = frame.GetFileName();
            var line = frame.GetFileLineNumber();
            if (string.IsNullOrEmpty(filePath) || line <= 0)
            {
                continue;
            }

            try
            {
                var snippet = _cache.GetOrCreate<SourceSnippet?>((filePath, line, opts.ContextLines), entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                    return ReadLocal(filePath, line, opts.ContextLines);
                });

                if (snippet is not null)
                {
                    return ValueTask.FromResult<SourceSnippet?>(snippet);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PLogger SourceLink: failed to resolve {File}:{Line}.", filePath, line);
            }
        }

        return ValueTask.FromResult<SourceSnippet?>(null);
    }

    /// <summary>
    /// AOT-safe namespace extraction. <see cref="StackFrame.GetMethod"/> is
    /// trim-unsafe (RequiresUnreferencedCode); <see cref="DiagnosticMethodInfo"/>
    /// returns just the metadata names without keeping the reflection graph alive.
    /// </summary>
    private static string? ResolveNamespace(StackFrame frame)
    {
        var info = DiagnosticMethodInfo.Create(frame);
        var declaringType = info?.DeclaringTypeName;
        if (string.IsNullOrEmpty(declaringType))
        {
            return null;
        }

        // DeclaringTypeName looks like "MyApp.Sub.Foo" or "MyApp.Sub.Outer+Inner".
        // The namespace is everything before the last '.', excluding the type-name suffix.
        var nestedSeparator = declaringType.IndexOf('+', StringComparison.Ordinal);
        var typeOnly = nestedSeparator >= 0 ? declaringType.AsSpan(0, nestedSeparator) : declaringType.AsSpan();
        var lastDot = typeOnly.LastIndexOf('.');
        return lastDot > 0 ? new string(typeOnly[..lastDot]) : null;
    }

    private static bool IsNamespaceAllowed(string? @namespace, IList<string> allowed)
    {
        if (allowed.Count == 0)
        {
            // No allow-list configured = nothing is allowed (defense-in-depth default).
            return false;
        }

        if (string.IsNullOrEmpty(@namespace))
        {
            return false;
        }

        foreach (var pattern in allowed)
        {
            if (pattern.EndsWith(".*", StringComparison.Ordinal))
            {
                var prefix = pattern.AsSpan(0, pattern.Length - 1); // keep trailing dot
                if (@namespace.AsSpan().StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            else if (pattern.Equals(@namespace, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static SourceSnippet? ReadLocal(string filePath, int lineNumber, int contextLines)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        var lines = File.ReadAllLines(filePath);
        if (lines.Length == 0 || lineNumber > lines.Length)
        {
            return null;
        }

        var startLine = Math.Max(1, lineNumber - contextLines);
        var endLine = Math.Min(lines.Length, lineNumber + contextLines);
        var span = new System.Text.StringBuilder(256);
        for (var i = startLine; i <= endLine; i++)
        {
            var marker = i == lineNumber ? "→ " : "  ";
            span.Append(marker)
                .Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append(": ")
                .AppendLine(lines[i - 1]);
        }

        return new SourceSnippet(filePath, lineNumber, span.ToString().TrimEnd('\r', '\n'),
            GitBlameAuthor: null, CommitSha: null);
    }
}
