// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

namespace InvertedSoftware.PLogger.Diagnostics.SourceLink;

/// <summary>
/// Resolves a stack frame to a <see cref="SourceSnippet"/> if the
/// originating source file is reachable (locally or via Source Link).
/// Returns null when the frame is filtered out, source isn't found, or
/// the namespace is outside the allow-list.
/// </summary>
public interface ISourceLinkResolver
{
    ValueTask<SourceSnippet?> ResolveAsync(Exception exception, CancellationToken cancellationToken);
}
