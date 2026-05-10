# InvertedSoftware.PLogger

[![Build status](https://img.shields.io/badge/build-passing-brightgreen)](#)
[![Tests](https://img.shields.io/badge/tests-71%20passed-brightgreen)](#)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-blueviolet)](https://dotnet.microsoft.com/)

> **From log sink to diagnostic engine.** Sentry, Seq, App Insights, and Datadog tell you *what* broke and *where*. PLogger 2.0 tells you *what to change in code to fix it* — every error becomes a structured diagnostic with an LLM-generated root-cause hypothesis, a concrete proposed code change as a unified diff, and a confidence/risk score, queryable via SQL **and exposed as an MCP server** so Claude Code, Copilot, or any agent can pull production diagnostics directly into the editor.

---

## Table of contents

- [What's new in 2.0](#whats-new-in-20)
- [Architecture at a glance](#architecture-at-a-glance)
- [Solution layout](#solution-layout)
- [Quick start](#quick-start)
- [Configuration reference](#configuration-reference)
- [Per-package guide](#per-package-guide)
  - [Core](#-invertedsoftwarepluggercore)
  - [Diagnostics](#-invertedsoftwarepluggerdiagnostics)
  - [Diagnostics.SqlServer](#-invertedsoftwarepluggerdiagnosticssqlserver)
  - [Diagnostics.SourceLink](#-invertedsoftwarepluggerdiagnosticssourcelink)
  - [Diagnostics.AI](#-invertedsoftwarepluggerdiagnosticsai)
  - [Diagnostics.Agents](#-invertedsoftwarepluggerdiagnosticsagents)
  - [Diagnostics.Mcp](#-invertedsoftwarepluggerdiagnosticsmcp)
  - [Diagnostics.GitHub / Slack / Teams](#-invertedsoftwarepluggerdiagnosticsgithub--slack--teams)
- [Sample app](#sample-app)
- [Building and testing](#building-and-testing)
- [Privacy and cost rails](#privacy-and-cost-rails)
- [Roadmap](#roadmap)
- [Upgrade guide from 1.x](#upgrade-guide-from-1x)
- [License](#license)

---

## What's new in 2.0

PLogger 2.0 is a ground-up modernization of the 1.x library while remaining a **drop-in upgrade** for existing consumers. The original `appsettings.json` keys all keep working unchanged, and `loggerFactory.AddPLogger(Configuration)` still does what it always did.

| Area | Change |
|---|---|
| **Target framework** | `net10.0` only. C# 14, `Nullable` enabled, `TreatWarningsAsErrors`. |
| **Hot path** | Synchronous `BlockingCollection` replaced with a bounded `Channel<LogEvent>` (capacity 8192, `DropOldest`, `SingleReader=true`). The hot path performs **no I/O, no blocking, and one allocation per call** (the `LogEvent` itself, ~104 bytes — verified by BenchmarkDotNet). |
| **Drain** | `PLoggerDrainService : BackgroundService` is the single reader; on shutdown it drains pending entries with a 5-second grace period. Multiple sinks (file, SQL, diagnostic-capture, notifiers) fan out from the drain via `IPLogSink`. |
| **AOT-safe** | Every package marked `IsAotCompatible=true` is also genuinely trim-clean. No reflection-based JSON binding (source-generated `JsonSerializerContext`), no runtime regex compilation (`[GeneratedRegex]`), no `ConfigurationBinder.Bind` (hand-rolled `IConfigureOptions<T>` per package). |
| **Configuration** | Hot reload via `IOptionsMonitor<T>` for every options type, including the Diagnostics-side ones (each package registers a `ConfigurationChangeTokenSource<T>`). |
| **OTel correlation** | The provider captures `Activity.Current?.TraceId` / `SpanId` into every `LogEvent` automatically. No new dependency — uses the BCL `System.Diagnostics.Activity`. |
| **Diagnostic intelligence** | New `InvertedSoftware.PLogger.Diagnostics.*` package family layers fingerprint dedup, source-link resolution, LLM-driven root-cause analysis, multi-agent fix proposal with validation, an MCP server, and GitHub/Slack/Teams notifiers — all opt-in, all chained off the same fluent builder. |
| **Backward compatibility** | `PLoggerSettings`, every existing `appsettings.json` key, and every previous `AddPLogger(...)` overload continue to compile and run unchanged. Upgrading from 1.x is a `PackageReference` version bump. |

---

## Architecture at a glance

```
                                                   ┌──────────────────────────────┐
                                                   │      Microsoft.Extensions.AI │
                                                   │      IChatClient (provider   │
                                                   │      pluggable: OpenAI /     │
                                                   │      Azure / Ollama / Foundry│
                                                   │      Local / etc.)           │
                                                   └──────────────┬───────────────┘
                                                                  │
ILogger.LogError(ex,…)                                            ▼
        │                                          ┌──────────────────────────────┐
        ▼                                          │  Diagnostics.Agents          │
┌──────────────┐    bounded     ┌────────────────┐ │  Triage → Context →          │
│   PLogger    │── Channel ────▶│ PLoggerDrain   │ │  Diagnosis → Fix →           │
│  (hot path)  │    (8192,      │ Service        │ │  Validator                   │
│              │    DropOldest) │  (single rdr)  │ └──────────────┬───────────────┘
│ <1 alloc/call│                │                │                │
│ no I/O       │                │  fan-out       │                ▼
│ no blocking  │                │  to N sinks ─┐ │ ┌──────────────────────────────┐
└──────────────┘                └──────────────┴─┴▶│  Diagnostics.SourceLink      │
        ▲                                          │  (PortablePDB → SourceSnippet)│
        │                                          └──────────────┬───────────────┘
        │                                                         ▼
   Activity.Current.TraceId / SpanId           ┌──────────────────────────────────┐
   (auto-captured)                             │   Diagnostics pipeline:          │
                                               │   capture sink → 2nd channel →   │
                                               │   enrich → vector dedup gate →   │
                                               │   diagnose → persist → notify    │
                                               └──────────────────┬───────────────┘
                                                                  │
                              ┌───────────────────────────────────┼─────────────────────┐
                              ▼                                   ▼                     ▼
              ┌──────────────────────────┐    ┌────────────────────────┐   ┌────────────────────┐
              │   File / SQL sink        │    │ Diagnostics.SqlServer  │   │ Notifiers (opt):   │
              │   (Phase 1)              │    │  plogger.Diagnostics   │   │  GitHub / Slack /  │
              │   plogger.txt /          │    │  + DiagnosticOcc.      │   │  Teams             │
              │   sproc                  │    │  + VECTOR(1536) for    │   │  (idempotent)      │
              └──────────────────────────┘    │  similarity dedup      │   └─────────┬──────────┘
                                              └───────────┬────────────┘             │
                                                          ▼                          ▼
                                              ┌─────────────────────────┐  ┌──────────────────┐
                                              │ Diagnostics.Mcp server  │  │  Webhook /       │
                                              │ (ASP.NET Core 10        │  │  GitHub issues   │
                                              │  endpoint at            │  │  with proposed   │
                                              │  /mcp/diagnostics)      │  │  diff + risk     │
                                              │                         │  └──────────────────┘
                                              │  Tools:                 │
                                              │   get_diagnosis         │  ◀───── Claude Code
                                              │   mark_fix_applied      │  ◀───── Copilot
                                              │   list_tools            │  ◀───── any MCP agent
                                              └─────────────────────────┘
```

**Key invariants** (apply at every layer):
1. **Logger invariant.** A failure anywhere downstream (enrichment, fingerprinting, AI, store, notifier) **never bubbles** into `ILogger.Log`. The basic log write always succeeds.
2. **Hot-path budget.** No I/O, no allocation beyond the `LogEvent`. All work happens in background services downstream of the channel.
3. **Backward compatibility.** Any 1.x consumer is a `PackageReference` bump.

---

## Solution layout

```
/src
  InvertedSoftware.PLogger.Core/                   the modernized provider — channel pipeline, sinks
  InvertedSoftware.PLogger.Diagnostics/            abstractions: records, interfaces, capture sink, pipeline service
  InvertedSoftware.PLogger.Diagnostics.SqlServer/  SQL Server 2025 store with VECTOR(1536) similarity search
  InvertedSoftware.PLogger.Diagnostics.SourceLink/ stack-frame → source-code resolver
  InvertedSoftware.PLogger.Diagnostics.AI/         IChatClient single-prompt diagnosis + redaction + token budget
  InvertedSoftware.PLogger.Diagnostics.Agents/     multi-agent pipeline (Triage / Context / Diagnosis / Fix / Validator)
  InvertedSoftware.PLogger.Diagnostics.Mcp/        MCP server exposing diagnostics to dev-time AI agents
  InvertedSoftware.PLogger.Diagnostics.GitHub/     GitHub issue + PR notifier with fingerprint-keyed idempotency
  InvertedSoftware.PLogger.Diagnostics.Slack/      Slack incoming-webhook notifier (Block Kit)
  InvertedSoftware.PLogger.Diagnostics.Teams/      Microsoft Teams incoming-webhook notifier (Adaptive Card)
/tests
  InvertedSoftware.PLogger.Core.Tests/             xUnit v3 — hot path, channel, drain, fingerprinter, back-compat
  InvertedSoftware.PLogger.Core.Benchmarks/        BenchmarkDotNet — LogError hot-path allocation + latency
  InvertedSoftware.PLogger.Diagnostics.Tests/      unit + Testcontainers SQL Server integration tests
/samples
  InvertedSoftware.PLogger.Sample.Web/             minimal-API .NET 10 demo (/health, /log, /throw, /diagnose, /mcp/...)
```

Apache-2.0 licensed; central-package-managed via `Directory.Packages.props`.

---

## Quick start

### 1. Install (Phase 1 only — drop-in v1 upgrade)

```bash
dotnet add package InvertedSoftware.PLogger.Core --version 2.0.0-preview.1
```

### 2. Wire it up in `Program.cs`

```csharp
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddPLogger();   // reads "Logging:PLogger" section of appsettings.json
var app = builder.Build();
```

Or via the v1 surface (`ILoggerFactory`):

```csharp
loggerFactory.AddPLogger(Configuration);
```

### 3. Configure

`appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": { "Default": "Information" },
    "PLogger": {
      "PLogType": "file",
      "BaseNameFile": "plogger.txt",
      "PLogFileMaxSizeKB": 1024
    }
  }
}
```

That's it — every error logged via `ILogger` flows through the channel, lands in `plogger.txt`, with OTel `TraceId`/`SpanId` correlation captured automatically.

### 4. (Optional) Enable diagnostics + AI + MCP + notifiers

```csharp
var diag = builder.Logging.AddPLogger()
    .AddPLoggerDiagnostics()                        // Phase 2: capture pipeline
    .AddSqlServerStore()                            // Phase 2: SQL Server 2025 store
    .AddPLoggerSourceLink(o => o.AllowedNamespaces  // Phase 3: source resolver
        = new[] { "MyCompany.*" })
    .AddPLoggerAi()                                 // Phase 3: single-prompt LLM
    // .AddPLoggerMultiAgent()                      // Phase 4: replaces single-prompt with Triage/Diagnosis/Fix/Validator
    .AddPLoggerMcpServer()                          // Phase 5: MCP endpoint
    .AddGitHubNotifier()                            // Phase 6: file issues / PRs
    .AddSlackNotifier()                             //         Block Kit cards
    .AddTeamsNotifier();                            //         Adaptive Cards

// (caller registers the IChatClient via Microsoft.Extensions.AI separately)
builder.Services.AddChatClient(/* OpenAI / Azure / Ollama / etc. */);
```

Each `Add*` is **opt-in** and reads its own configuration section under `Logging:PLogger:Diagnostics`. Calling none of them = identical behaviour to PLogger 1.x.

---

## Configuration reference

Every configuration value below is bound through hand-rolled `IConfigureOptions<T>` (no `ConfigurationBinder.Bind` reflection) and hot-reloads via `IOptionsMonitor<T>` change-token sources.

### `Logging:PLogger` — Core (Phase 1)

| Key | Description | Default |
|---|---|---|
| `PLogType` | `file` or anything else (treated as SQL) | `file` |
| `PLogEnabled` | Master enable switch | `true` |
| `BaseNameFile` | Filename for the rolling-file sink | `plogger.txt` |
| `PLogFileMaxSizeKB` | Maximum size before rolling to a new file | `1024` |
| `PLogFileMessageTemplate` | `String.Format` template applied to every log message | `"{0}"` |
| `StringConnection` | SQL connection string (file mode ignores this) | `ErrorsConnection` |
| `StoredProcedureName` | Sproc invoked once per log entry | `AddError` |
| `MessageParameterName` | Parameter name in the sproc that receives the rendered message | `@ErrorName` |
| `PLogDeleteFilesOlderThanDays` | If > 0, sweep + delete files older than N days every 24h | `0` |
| `PLogStopLoggingIfSpaceSmallerThanMB` | If > 0, gate writes on `>= N MB` of free disk space | `0` |
| `ChannelCapacity` | Bounded `Channel<LogEvent>` capacity | `8192` |
| `DiagnosticChannelCapacity` | Capacity of the secondary diagnostic channel (Phase 2+) | `1024` |

### `Logging:PLogger:Diagnostics` — Diagnostic pipeline (Phase 2+)

| Key | Description | Default |
|---|---|---|
| `Enabled` | Master switch — false means no diagnostic services run | `false` |
| `AppName` | Logical app name (flows to `plogger.Diagnostics.AppName`) | `default` |
| `Environment` | Environment label (`Production`, `Staging`, …) | `Production` |
| `MinSeverity` | Minimum severity to capture (rejects `None` to prevent silent disable) | `Error` |
| `ChannelCapacityOverride` | Override `DiagnosticChannelCapacity` for the secondary channel | `null` |
| `ShutdownTimeout` | Time the pipeline service drains pending entries on shutdown | `00:00:05` |

### `Logging:PLogger:Diagnostics:Storage` — SQL Server store (Phase 2)

| Key | Description | Default |
|---|---|---|
| `ConnectionString` | SQL Server connection string | (empty) |
| `ApplySchemaOnStartup` | If true, apply embedded `0001_initial.sql` on host start | `true` |
| `CommandTimeoutSeconds` | Per-command timeout for the diagnostic store | `5` |

### `Logging:PLogger:Diagnostics:SourceLink` — Source resolver (Phase 3)

| Key | Description | Default |
|---|---|---|
| `Enabled` | Master switch | `true` |
| `GitHubToken` | GitHub PAT used for raw fetches (public repos work without) | `null` |
| `AllowedNamespaces` | Defense-in-depth list — only resolve frames in these namespaces | `[]` (empty = nothing resolves) |
| `ContextLines` | Lines above + below the implicated line included in the snippet | `5` |
| `FetchTimeout` | Per-resolve HTTP timeout for remote fetches | `00:00:03` |

### `Logging:PLogger:Diagnostics:AI` — LLM diagnosis (Phase 3)

| Key | Description | Default |
|---|---|---|
| `Enabled` | Master switch | `false` |
| `Model` | Model identifier passed to `IChatClient` | `gpt-5-mini` |
| `TokenBudgetPerMinute` | Sliding-window per-app per-minute cap (0 = unlimited) | `50000` |
| `MaxTokensPerCall` | Cap per individual diagnosis | `2000` |
| `VectorSimilarityThreshold` | Cosine ≥ this short-circuits the LLM call | `0.92` |
| `CallTimeout` | Per-call wall-clock | `00:00:30` |

### `Logging:PLogger:Diagnostics:Agents` — Multi-agent pipeline (Phase 4)

| Key | Description | Default |
|---|---|---|
| `TriageModel` | Cheap model for the triage classifier | `gpt-5-mini` |
| `FrontierModel` | Frontier model for Context / Diagnosis / Fix | `gpt-5` |
| `MaxTokensPerAgent` | Per-stage budget cap | `1500` |
| `FixRetryCount` | If validator rejects the diff, how many fix retries | `1` |
| `CallTimeout` | Per-stage wall-clock | `00:00:30` |

### `Logging:PLogger:Diagnostics:Mcp` — MCP server (Phase 5)

| Key | Description | Default |
|---|---|---|
| `Enabled` | Mounts the endpoint when true | `false` |
| `RoutePrefix` | URL prefix the MCP transport binds to | `/mcp/diagnostics` |
| `BearerToken` | Required `Authorization: Bearer …` value (constant-time compared) | `null` (= no auth) |

### `Logging:PLogger:Diagnostics:Notifiers:GitHub` — GitHub issue notifier (Phase 6)

| Key | Description | Default |
|---|---|---|
| `Enabled` | Master switch | `false` |
| `Repository` | `owner/repo` | (empty) |
| `Token` | GitHub PAT with `issues:write` | (empty) |
| `Labels` | Labels applied to filed issues | `["plogger", "auto-filed"]` |
| `MinOccurrenceCount` | Don't file until the diagnostic has fired N times | `1` |
| `RequestTimeout` | Per-request HTTP timeout | `00:00:15` |

### `Logging:PLogger:Diagnostics:Notifiers:Slack` — Slack notifier (Phase 6)

| Key | Description | Default |
|---|---|---|
| `Enabled` | Master switch | `false` |
| `WebhookUrl` | Slack incoming-webhook URL | (empty) |
| `AppDisplayName` | Shown in the card title | `PLogger` |
| `MinOccurrenceCount` | Threshold before posting | `1` |
| `RequestTimeout` | Per-request HTTP timeout | `00:00:10` |

### `Logging:PLogger:Diagnostics:Notifiers:Teams` — Microsoft Teams notifier (Phase 6)

| Key | Description | Default |
|---|---|---|
| `Enabled` | Master switch | `false` |
| `WebhookUrl` | Teams incoming-webhook URL | (empty) |
| `AppDisplayName` | Shown in the card title | `PLogger` |
| `MinOccurrenceCount` | Threshold before posting | `1` |
| `RequestTimeout` | Per-request HTTP timeout | `00:00:10` |

---

## Per-package guide

### 📦 InvertedSoftware.PLogger.Core

The provider. Contains `PLogger` (hot path), `PLoggerProvider` (`ILoggerProvider`), `PLoggerChannel` (bounded `Channel<LogEvent>`), `PLoggerDrainService` (single-reader `BackgroundService`), `RollingFileLogSink`, `SqlLogSink`, `LogEvent`, `LogEventFormatter`, `StackFingerprinter`, every legacy `AddPLogger(...)` overload preserved.

```csharp
builder.Logging.AddPLogger();
```

#### `LogEvent` — what crosses the channel

```csharp
public sealed record LogEvent(
    DateTimeOffset TimestampUtc,
    LogLevel Level,
    string Category,
    EventId EventId,
    string Message,
    Exception? Exception,
    IReadOnlyList<KeyValuePair<string, object?>> Scope,
    string? TraceId,
    string? SpanId);
```

#### `StackFingerprinter` — stable exception identity

```csharp
using InvertedSoftware.PLogger.Core.Fingerprinting;

byte[] hash         = StackFingerprinter.Compute(ex);    // 16 bytes (xxHash3, BINARY(16) compatible)
string normalized   = StackFingerprinter.Normalize(ex);  // input to the hash; safe to log
```

Normalization rules:
- Strips line numbers, IL offsets, file paths.
- Collapses generic args to arity (`List`1[Int32]` → `List`1`).
- Strips lambda display classes (`<>c__DisplayClass3_0` → `<display>`).
- Strips async state-machine machinery (`<Method>d__N`, `MoveNext`, Mono `<>c__AnonStorey\d+`, `<>c__Iterator\d+`, etc.).
- Walks `InnerException` chain with a delimiter.

Two exceptions with identical normalized stacks fingerprint identically across runs, machines, and CoreCLR ↔ Mono.

---

### 📦 InvertedSoftware.PLogger.Diagnostics

Abstractions: `LogEvent` enrichment to `DiagnosticContext`, fingerprinting, the `IDiagnosticPipeline` orchestrator, `DiagnosticCaptureSink` (the second sink that pushes onto a separate channel), `DiagnosticPipelineService` (its single reader), `IDiagnosticEnricher` / `IDiagnosticAgent` / `IDiagnosticStore` / `IDiagnosticNotifier` / `ITokenBudgetEnforcer` / `IPiiScanner` extension points.

```csharp
builder.Logging.AddPLogger().AddPLoggerDiagnostics(o =>
{
    o.AppName = "OrderService";
    o.Environment = "Production";
    o.MinSeverity = LogLevel.Error;
});
```

---

### 📦 InvertedSoftware.PLogger.Diagnostics.SqlServer

SQL Server 2025 backing store. Idempotent schema (`plogger.Diagnostics` + `plogger.DiagnosticOccurrences`), `MERGE`-based upsert keyed on `(AppName, Environment, Fingerprint)`, `OccurrenceCount` increments on dedup hit, native `VECTOR(1536)` for similarity search via `JSON_ARRAY_TO_VECTOR(@vec, 1536)` + `VECTOR_DISTANCE('cosine', col, vec)`.

```csharp
builder.Logging.AddPLogger().AddPLoggerDiagnostics()
    .AddSqlServerStore(o =>
    {
        o.ConnectionString = "Server=...;Database=...;Authentication=Active Directory Default";
        o.ApplySchemaOnStartup = true;
    });
```

The schema file is embedded as `Schema/0001_initial.sql` and applied idempotently at host start; it's also safe to apply via your own migration tooling (it uses `IF NOT EXISTS` guards everywhere).

---

### 📦 InvertedSoftware.PLogger.Diagnostics.SourceLink

Walks `Exception.StackTrace`, gates frames by `AllowedNamespaces` (defense-in-depth — empty allow-list = nothing resolves), reads the local source span when reachable. AOT-safe namespace extraction via `System.Diagnostics.DiagnosticMethodInfo` (the trim-safe alternative to `StackFrame.GetMethod()`).

```csharp
builder.Logging.AddPLogger().AddPLoggerDiagnostics()
    .AddPLoggerSourceLink(o =>
    {
        o.AllowedNamespaces = new[] { "MyCompany.*" };
        o.ContextLines = 5;
    });
```

When registered, replaces the default `IDiagnosticEnricher` with one that populates `DiagnosticContext.Source` for any frame inside the allow-list whose source file is reachable on disk.

---

### 📦 InvertedSoftware.PLogger.Diagnostics.AI

Single-prompt LLM diagnosis on top of `Microsoft.Extensions.AI`'s `IChatClient`.

- **Redaction** via `[GeneratedRegex]` patterns: AWS / Azure / GCP / generic Bearer API keys, JWTs, ADO.NET / MongoDB / PostgreSQL connection strings, password key/value pairs, emails, IPv4/IPv6, SSNs, Luhn-validated credit cards. Patterns are deliberately over-aggressive — false positives become `<redacted-{kind}>` tags; false negatives are never tolerated.
- **Token budget** via `ITokenBudgetEnforcer` (sliding 60-second window, lock-free reservation, refundable on cancellation).
- **Structured outputs** via `JsonSerializerContext` source generators — no regex parsing of LLM responses.
- **Vector dedup gate** — `IDiagnosticStore.FindSimilarAsync(embedding, threshold=0.92)` short-circuits the LLM call when a similar prior diagnosis exists.
- **Startup validator** that throws an actionable error message at host start if `IChatClient` isn't registered.

```csharp
builder.Services.AddChatClient(/* OpenAI / Azure / Ollama / Foundry Local */);
builder.Logging.AddPLogger().AddPLoggerDiagnostics()
    .AddSqlServerStore()
    .AddPLoggerSourceLink(o => o.AllowedNamespaces = new[] { "MyCompany.*" })
    .AddPLoggerAi(o =>
    {
        o.Model = "gpt-5-mini";
        o.TokenBudgetPerMinute = 100_000;
    });
```

> **AOT note:** This package is `IsAotCompatible=false` because `Microsoft.Extensions.AI` uses reflection on response shapes. Every other Diagnostics package stays AOT-clean.

---

### 📦 InvertedSoftware.PLogger.Diagnostics.Agents

Replaces `AiDiagnosisAgent` with a five-stage orchestrator: **Triage → Context → Diagnosis → Fix → Validator**.

| Agent | Model | Output |
|---|---|---|
| `TriageAgent` | cheap (`gpt-5-mini`) | `ErrorCategory` + `DeepAnalysisWarranted` flag — short-circuits transient errors before any frontier call |
| `ContextAgent` | (none in Phase 4) | enriched `DiagnosticContext` (full tool surface lands in Phase 4.x) |
| `DiagnosisAgent` | frontier (`gpt-5`, `claude-opus-4-7`, …) | `RootCauseHypothesis` |
| `FixAgent` | frontier | `ProposedFix { FilePath, UnifiedDiff, Rationale, Risk, RegressionConcerns[] }` |
| `ValidatorAgent` | **pure logic, no LLM** | `ValidationResult` — checks the diff parses, targets only the implicated file, agrees with the StackFrame anchor |

If `ValidatorAgent` rejects, the orchestrator can retry the `FixAgent` once (configurable). Each stage is gated by `ITokenBudgetEnforcer`.

```csharp
.AddPLoggerMultiAgent(o =>
{
    o.TriageModel = "gpt-5-mini";
    o.FrontierModel = "claude-opus-4-7";
    o.FixRetryCount = 1;
})
```

---

### 📦 InvertedSoftware.PLogger.Diagnostics.Mcp

MCP server hosted in ASP.NET Core 10 minimal API. The marketing wedge: when a developer asks Claude Code "fix the bug in the order service," the agent calls these tools and pulls full root-cause hypotheses + proposed diffs into the editor.

**Tools exposed:**

| Tool | Description |
|---|---|
| `get_diagnosis(fingerprintHex)` | Full `DiagnosticRecord` including hypothesis + proposed diff |
| `mark_fix_applied(fingerprintHex, commitSha)` | Closes the feedback loop; future occurrences are deprioritised |
| `list_tools()` | Health probe + advertises which tools this server implements |

(Phase 5.x adds `get_recent_diagnostics`, `find_similar`, `list_unresolved`, `get_recent_logs`.)

**Wire it up** in any ASP.NET Core 10 app:

```csharp
builder.Logging.AddPLogger().AddPLoggerDiagnostics()
    .AddSqlServerStore(o => o.ConnectionString = "...")
    .AddPLoggerMcpServer(o =>
    {
        o.Enabled = true;
        o.RoutePrefix = "/mcp/diagnostics";
        o.BearerToken = builder.Configuration["MCP_BEARER"]; // constant-time compared
    });

var app = builder.Build();
app.MapPLoggerMcp();   // mounts at the configured route prefix
```

**Connect Claude Code** to the running server with this `.mcp.json`:

```json
{
  "mcpServers": {
    "plogger": {
      "type": "http",
      "url": "https://your-app.example.com/mcp/diagnostics",
      "headers": {
        "Authorization": "Bearer YOUR_TOKEN_HERE"
      }
    }
  }
}
```

---

### 📦 InvertedSoftware.PLogger.Diagnostics.GitHub / Slack / Teams

Notifier sinks that the pipeline calls after a successful persist. All three are independently optional and fan out in parallel; one failing notifier never blocks the others.

#### GitHub — file issues, idempotently

Title carries a `[plogger-fp:<HEX>]` tag the search API uses to find existing issues before creating a new one. Per-request `HttpRequestMessage` headers (no shared `DefaultRequestHeaders` mutation, so concurrent fan-outs can't race or leak the bearer token).

```csharp
.AddGitHubNotifier(o =>
{
    o.Repository = "myorg/myapp";
    o.Token = builder.Configuration["GITHUB_TOKEN"];
    o.Labels = new[] { "plogger", "auto-filed", "production" };
    o.MinOccurrenceCount = 5;   // wait until it's actually a recurring issue
})
```

#### Slack — Block Kit cards

```csharp
.AddSlackNotifier(o =>
{
    o.WebhookUrl = builder.Configuration["SLACK_WEBHOOK"];
    o.AppDisplayName = "OrderService Production";
})
```

#### Microsoft Teams — Adaptive Cards

```csharp
.AddTeamsNotifier(o =>
{
    o.WebhookUrl = builder.Configuration["TEAMS_WEBHOOK"];
})
```

All three notifiers also use a shared bounded LRU `RecentSet` (15-min TTL, 256 entries) to dedupe rapid in-process repeats of the same fingerprint between channel-flush cycles — the SQL store does the authoritative dedup; this just stops a tight error loop from spamming external endpoints.

---

## Sample app

`samples/InvertedSoftware.PLogger.Sample.Web` is a minimal-API .NET 10 demo that exercises every layer.

```bash
cd samples/InvertedSoftware.PLogger.Sample.Web
dotnet run -c Release
```

Endpoints:

| Path | What it does |
|---|---|
| `GET /` | Documentation index |
| `GET /health` | Returns `{ "status": "ok" }` |
| `GET /log` | Emits one Information log entry |
| `GET /throw` | Throws + catches → `LogError` → returns 500 |
| `GET /throw/fingerprint` | Returns the canonical fingerprint hex for `/throw` (deterministic across runs) |
| `GET /diagnostics/{fingerprintHex}` | Reads the persisted `DiagnosticRecord` from the store |
| `GET /diagnose` | Runs enricher + agent **inline** so the AI flow is exercisable without waiting for the channel |
| `*` `/mcp/diagnostics/...` | MCP transport when `Logging:PLogger:Diagnostics:Mcp:Enabled=true` |

End-to-end dedup demo:

```bash
curl http://localhost:5099/throw/fingerprint
# {"fingerprintHex":"3E1DF2C149245AABA0D1C06EC6F99722"}

for i in {1..50}; do curl -s -o /dev/null http://localhost:5099/throw; done

curl http://localhost:5099/diagnostics/3E1DF2C149245AABA0D1C06EC6F99722
# DiagnosticRecord with OccurrenceCount=50, FirstSeenUtc, LastSeenUtc, ...
```

---

## Building and testing

Requires .NET 10 SDK (`global.json` pins `10.0.100` with `latestMinor` roll-forward).

```bash
dotnet build InvertedSoftware.PLogger.slnx -c Release
dotnet test  InvertedSoftware.PLogger.slnx -c Release
```

Currently: **9 packages, 71 tests passed, 1 skipped (Docker-dependent SQL Server integration test), 0 failed, 0 build warnings.**

Run the BenchmarkDotNet suite:

```bash
dotnet run --project tests/InvertedSoftware.PLogger.Core.Benchmarks -c Release -- --filter '*HotPath*'
```

Hot-path target — verified: **104 bytes per `LogError` call (just the `LogEvent` record), no Gen1 pressure under sustained load.**

The Testcontainers SQL Server integration test (`SqlServerStoreIntegrationTests`) runs against `mcr.microsoft.com/mssql/server:2022-latest`; it's auto-skipped when the Docker daemon isn't reachable. To exercise it, install Docker Desktop (Windows / macOS) or a Docker Engine (Linux) and re-run `dotnet test`.

### Publish for native AOT

The library packages that are AOT-clean (`Core`, `Diagnostics`, `Diagnostics.SqlServer`'s analyzer pass aside, `Diagnostics.SourceLink`, `Diagnostics.GitHub`, `Diagnostics.Slack`, `Diagnostics.Teams`) all carry `<IsAotCompatible>true</IsAotCompatible>`. The application:

```bash
dotnet publish samples/InvertedSoftware.PLogger.Sample.Web -c Release -p:PublishAot=true
```

(Requires the Visual Studio Desktop Development for C++ workload on Windows for the linker.)

The `Diagnostics.AI`, `Diagnostics.Agents`, and `Diagnostics.Mcp` packages opt out of AOT (their dependencies — Microsoft.Extensions.AI, the MCP SDK — use reflection on response shapes). Including them in an AOT-published app is not currently supported.

---

## Privacy and cost rails

Hard requirements baked into Phase 3+ — without these, the framework is unshippable in regulated industries.

| Rail | Where |
|---|---|
| **Source redaction** | `RedactionEngine` strips secrets + PII *before* any LLM call. Defense-in-depth: namespace allow-list in `SourceLinkOptions.AllowedNamespaces` filters frames *before* the redaction layer runs. |
| **Secret scanning** | `[GeneratedRegex]` covers AWS/Azure/GCP keys, JWTs, connection strings, password=, email, IPv4/IPv6, SSN, Luhn-valid credit cards. Patterns extensible per-app via DI. |
| **PII scrubbing** | `IPiiScanner` plug-in point for Microsoft Presidio (or any NER pipeline) for the long-tail PII the regex layer can't express. |
| **Local-first LLM option** | The `IChatClient` abstraction lets you point at Ollama, Foundry Local, or any local model — no calls to external APIs. Required for HIPAA / PCI / FedRAMP shops where Sentry / Datadog can't go. |
| **Token budget** | Per-app, per-minute ceiling enforced *before* any LLM call. Hard stop at limit; refundable on cancellation / network failure. |
| **Dedup before LLM** | Fingerprint dedup + vector similarity gate eliminates 80–95% of LLM calls in steady state. |
| **Synchronous fail-safe** | The "logger invariant" — if the diagnostic pipeline is down, broken, or over budget, the underlying log write **always succeeds**. PLogger is a logger first, an oracle second. |
| **Constant-time auth** | MCP bearer-token compare uses `CryptographicOperations.FixedTimeEquals` over UTF-8 bytes (length-prechecked) — no timing oracle on the prefix. |

---

## Roadmap

| Phase | Status | Notes |
|---|---|---|
| **1** — .NET 10 modernization | ✅ shipped | drop-in upgrade from 1.x |
| **2** — SQL Server 2025 store + dedup | ✅ shipped | "this error fired 4,213 times today, first seen 2 weeks ago" — already useful with no AI |
| **3** — IChatClient diagnosis + redaction + Source Link | ✅ shipped | first "wow" demo — single-prompt pipeline |
| **4** — Multi-agent orchestrator | ✅ shipped | Triage → Context → Diagnosis → Fix → Validator |
| **5** — MCP server | ✅ shipped | `get_diagnosis`, `mark_fix_applied`, `list_tools` (Phase 5.x adds the rest) |
| **6** — GitHub / Slack / Teams notifiers | ✅ shipped | idempotent, per-request HTTP headers |
| **7** — IDE extensions | 📋 separate repo | VSIX + JetBrains Marketplace; consume the MCP server from Phase 5 |

**Phase 4.x** (next milestone): expose the full ContextAgent tool surface (`ReadSourceFile`, `FindSimilarErrors`, `GetGitBlame`, `GetOtelTrace`, `GetRecentLogs`) so the diagnosis stage has rich grounding rather than just the source span.

**Phase 5.x**: complete the MCP tool surface (`get_recent_diagnostics`, `find_similar`, `list_unresolved`, `get_recent_logs`).

---

## Upgrade guide from 1.x

For consumers of PLogger 1.x: **upgrading is a `PackageReference` version bump.** No code changes, no config changes.

```xml
<PackageReference Include="InvertedSoftware.PLogger.Core" Version="2.0.0-preview.1" />
```

Every key in the original `appsettings.json` keeps working unchanged. Every existing `AddPLogger(...)` overload (on `ILoggerFactory` and `ILoggingBuilder`, with and without `IConfiguration` / `PLoggerSettings` / `LogLevel` parameters) keeps the same signatures. `PLoggerSettings` itself remains as a back-compat shim around the new `PLoggerOptions`.

Original v1 configuration table — **kept verbatim** for documentation continuity:

| Parameter | Description | Value |
| --- | --- | --- |
| PLogType | The type of logger you are initiating | file or database |
| PLogEnabled | Enable logging | true or false |
| BaseNameFile | The name of the file to log to | applog.txt |
| PLogFileMaxSizeKB | The maximum size of a log file before starting a new file | 1024 |
| PLogFileMessageTemplate | A String.Format message template | "Loge Entry: {0}" |
| StringConnection | The string connection to your logging database | "Server=myServerAddress;Database=myDataBase;User Id=myUsername;Password=myPassword;" |
| StoredProcedureName | The Sproc to use when logging to database | "SprocName" |
| MessageParameterName | The Sproc Paramter Name | "@MyParamter" |
| PLogDeleteFilesOlderThanDays | Delete old log files | 0 to not delete. Otherwise number of days |
| PLogStopLoggingIfSpaceSmallerThanMB | Stop if disk is full | 0 if dont stop. Otherwise a long |

---

## License

Apache-2.0 — see [LICENSE](LICENSE). Copyright 2020 Inverted Software (Gal Ratner).

The patent grant in Apache-2.0 is intentional given the agent / fix-proposal IP in Phase 3+.

## Contributing

Issues and pull requests welcome at [github.com/invertedsoftware/InvertedSoftware.PLogger.Core](https://github.com/invertedsoftware/InvertedSoftware.PLogger.Core).

Conventional-commit messages preferred (`feat(core): …`, `fix(diagnostics): …`, `docs: …`). Each commit should pass `dotnet build -c Release` (zero warnings — `TreatWarningsAsErrors` is on) and `dotnet test`.
