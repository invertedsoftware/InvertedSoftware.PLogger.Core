// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

namespace InvertedSoftware.PLogger.Diagnostics.Slack;

public sealed class SlackNotifierOptions
{
    public const string SectionName = "Logging:PLogger:Diagnostics:Notifiers:Slack";

    public bool Enabled { get; set; }
    public string WebhookUrl { get; set; } = string.Empty;
    public string AppDisplayName { get; set; } = "PLogger";
    public long MinOccurrenceCount { get; set; } = 1;
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
