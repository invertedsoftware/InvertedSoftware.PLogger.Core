// Copyright (c) Inverted Software. All rights reserved.

using System.Text.Json.Serialization;

namespace InvertedSoftware.PLogger.Core.Internal;

/// <summary>
/// AOT-safe <see cref="JsonSerializerContext"/> for any types PLogger
/// itself needs to serialize (currently the bound options shape, used by
/// the back-compat smoke test). Downstream packages add their own contexts.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified)]
[JsonSerializable(typeof(PLoggerOptions))]
[JsonSerializable(typeof(PLoggerSettings))]
internal sealed partial class PLoggerJsonContext : JsonSerializerContext;
