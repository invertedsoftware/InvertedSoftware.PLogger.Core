// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using System.Text.Json.Serialization;

namespace InvertedSoftware.PLogger.Diagnostics.Internal;

/// <summary>
/// AOT-safe <see cref="JsonSerializerContext"/> for the persisted shape
/// of a diagnosis. Used by the SQL store + the MCP server when
/// serializing or deserializing the <c>Diagnosis</c> column.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DiagnosisResult))]
[JsonSerializable(typeof(RootCauseHypothesis))]
[JsonSerializable(typeof(ProposedFix))]
[JsonSerializable(typeof(ErrorCategory))]
[JsonSerializable(typeof(RiskLevel))]
public sealed partial class DiagnosticsJsonContext : JsonSerializerContext;
