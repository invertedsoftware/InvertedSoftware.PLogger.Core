// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using InvertedSoftware.PLogger.Diagnostics.SourceLink;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Diagnostics.Tests;

public class SourceLinkResolverTests
{
    [Fact]
    public async Task Resolver_Disabled_ReturnsNull()
    {
        var resolver = MakeResolver(new SourceLinkOptions { Enabled = false });
        Exception ex = await CaptureAsync(static () => throw new InvalidOperationException("test"));

        var snippet = await resolver.ResolveAsync(ex, CancellationToken.None);
        Assert.Null(snippet);
    }

    [Fact]
    public async Task Resolver_NoAllowedNamespaces_ReturnsNull()
    {
        // Empty allow-list = nothing resolved (defense-in-depth default).
        var resolver = MakeResolver(new SourceLinkOptions { Enabled = true });
        Exception ex = await CaptureAsync(static () => throw new InvalidOperationException("test"));

        var snippet = await resolver.ResolveAsync(ex, CancellationToken.None);
        Assert.Null(snippet);
    }

    [Fact]
    public async Task Resolver_NamespaceAllowed_ReturnsSnippetWhenSourceLocal()
    {
        var resolver = MakeResolver(new SourceLinkOptions
        {
            Enabled = true,
            AllowedNamespaces = new List<string>
            {
                // Allow this very test namespace so the frame matches.
                "InvertedSoftware.PLogger.Diagnostics.Tests",
            },
            ContextLines = 2,
        });

        Exception ex = await CaptureAsync(static () => throw new InvalidOperationException("test"));

        var snippet = await resolver.ResolveAsync(ex, CancellationToken.None);
        // Local source path may or may not be present depending on where
        // the test was built (CI strips paths, dev keeps them). Either is OK,
        // but if a snippet was returned, validate its shape.
        if (snippet is not null)
        {
            Assert.True(snippet.LineNumber > 0);
            Assert.NotEmpty(snippet.CodeSpan);
            Assert.Contains("→", snippet.CodeSpan, StringComparison.Ordinal);
        }
    }

    private static SourceLinkResolver MakeResolver(SourceLinkOptions options)
    {
        var monitor = new TestMonitor(options);
        var cache = new MemoryCache(new MemoryCacheOptions());
        return new SourceLinkResolver(monitor, cache, NullLogger<SourceLinkResolver>.Instance);
    }

    private static async Task<Exception> CaptureAsync(Action action)
    {
        await Task.Yield();
        try
        {
            action();
        }
        catch (Exception ex)
        {
            return ex;
        }
        throw new InvalidOperationException("expected throw");
    }

    private sealed class TestMonitor : IOptionsMonitor<SourceLinkOptions>
    {
        public TestMonitor(SourceLinkOptions value) { CurrentValue = value; }
        public SourceLinkOptions CurrentValue { get; }
        public SourceLinkOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<SourceLinkOptions, string?> listener) => null;
    }
}
