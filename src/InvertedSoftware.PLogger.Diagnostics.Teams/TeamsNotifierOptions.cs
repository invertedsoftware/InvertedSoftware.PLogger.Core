// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

namespace InvertedSoftware.PLogger.Diagnostics.Teams;

public sealed class TeamsNotifierOptions
{
    public const string SectionName = "Logging:PLogger:Diagnostics:Notifiers:Teams";

    public bool Enabled { get; set; }
    public string WebhookUrl { get; set; } = string.Empty;
    public string AppDisplayName { get; set; } = "PLogger";
    public long MinOccurrenceCount { get; set; } = 1;
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
