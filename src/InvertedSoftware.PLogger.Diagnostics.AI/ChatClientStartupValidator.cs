// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace InvertedSoftware.PLogger.Diagnostics.AI;

/// <summary>
/// Hosted service that validates an <see cref="IChatClient"/> is registered
/// when <c>AddPLoggerAi()</c> (or the multi-agent pipeline) is wired up.
/// Fails fast at host start with a clear actionable message instead of
/// surfacing the same gap deep in the diagnostic pipeline at first
/// LogError() call.
/// </summary>
public sealed class ChatClientStartupValidator : IHostedService
{
    private readonly IServiceProvider _services;

    public ChatClientStartupValidator(IServiceProvider services)
    {
        _services = services;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var chat = _services.GetService<IChatClient>();
        if (chat is null)
        {
            throw new InvalidOperationException(
                "PLogger AI requires an IChatClient registered in DI. "
                + "Add one via Microsoft.Extensions.AI — e.g. services.AddChatClient(...) "
                + "with a provider package such as Azure.AI.OpenAI, OpenAI, or OllamaSharp. "
                + "See the Microsoft.Extensions.AI documentation for setup.");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
