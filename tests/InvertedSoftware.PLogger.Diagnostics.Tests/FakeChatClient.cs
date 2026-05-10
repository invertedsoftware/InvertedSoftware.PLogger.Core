// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using Microsoft.Extensions.AI;

namespace InvertedSoftware.PLogger.Diagnostics.Tests;

/// <summary>
/// Test double for <see cref="IChatClient"/>. Returns canned responses
/// keyed by call index — useful for sequenced multi-agent tests where
/// Triage / Diagnosis / Fix each get a different scripted reply.
/// </summary>
internal sealed class FakeChatClient : IChatClient
{
    private readonly Queue<string> _responses;
    public int CallCount { get; private set; }
    public List<ChatOptions?> SeenOptions { get; } = new();

    public FakeChatClient(params string[] responses)
    {
        _responses = new Queue<string>(responses);
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        SeenOptions.Add(options);
        var text = _responses.Count > 0 ? _responses.Dequeue() : "{}";
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("FakeChatClient does not support streaming.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
