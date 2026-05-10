# InvertedSoftware.PLogger.Core

Implementation of PLogger as a logging provider for .NET.

## What's new in 2.0 (preview)

PLogger 2.0 is a ground-up modernization of the 1.x library while remaining a **drop-in upgrade** for existing consumers — the existing `appsettings.json` keys all keep working, and `loggerFactory.AddPLogger(Configuration)` still does what it always did.

| Area | Change |
|---|---|
| Target framework | `net10.0` (was `netcoreapp3.1`). C# 14 with `Nullable` enabled. |
| Pipeline | Synchronous `BlockingCollection` replaced with a bounded `System.Threading.Channels.Channel<LogEvent>` (capacity 8192, `DropOldest` policy, `SingleReader=true`). The hot path performs **no I/O, no blocking, and one allocation per call** (the `LogEvent` itself). |
| Drain | A hosted `PLoggerDrainService : BackgroundService` is the single reader; on shutdown it drains pending entries with a 5-second grace period. |
| AOT | `IsAotCompatible=true` across all packages. No reflection-based JSON binding, no runtime regex compilation — file-rotation patterns and stack-trace normalization use `[GeneratedRegex]`; internal diagnostic logging uses `[LoggerMessage]`. |
| Configuration | Hot-reload via `IOptionsMonitor<PLoggerOptions>`. Bind hand-rolled (no `ConfigurationBinder.Bind` reflection) so the entire pipeline is trim/AOT-safe. |
| OTel correlation | The provider captures `Activity.Current?.TraceId` / `SpanId` into every `LogEvent` automatically. No new dependency — uses the BCL `System.Diagnostics.Activity`. |
| Stack fingerprinting | New `InvertedSoftware.PLogger.Core.Fingerprinting.StackFingerprinter` with stable `xxHash3`-based identity for exceptions across line-number changes, generic argument variation, lambda display classes, and async state-machine frames. Phase 2+ will use it for dedupe / similarity gating. |
| Backward compatibility | `PLoggerSettings`, every existing `appsettings.json` key, and every previous `AddPLogger(...)` overload continue to compile and run unchanged. Upgrading from 1.x is a `PackageReference` version bump. |

The diagnostic intelligence layer (root-cause hypothesis, source-link resolution, `IChatClient` integration, MCP server) lives in separate packages — `InvertedSoftware.PLogger.Diagnostics.*` — and is not active in 2.0 preview 1. Those land in subsequent phases.

### Solution layout

```
/src
  InvertedSoftware.PLogger.Core/                  modernized provider
  InvertedSoftware.PLogger.Diagnostics/           abstractions (placeholder, Phase 2)
  InvertedSoftware.PLogger.Diagnostics.SqlServer/ SQL 2025 + VECTOR(N) store (placeholder, Phase 2)
  InvertedSoftware.PLogger.Diagnostics.AI/        IChatClient integration (placeholder, Phase 3)
  InvertedSoftware.PLogger.Diagnostics.Agents/    multi-agent pipeline (placeholder, Phase 4)
  InvertedSoftware.PLogger.Diagnostics.Mcp/       MCP server (placeholder, Phase 5)
/tests
  InvertedSoftware.PLogger.Core.Tests/            xUnit v3
  InvertedSoftware.PLogger.Core.Benchmarks/       BenchmarkDotNet
/samples
  InvertedSoftware.PLogger.Sample.Web/            minimal-API .NET 10 smoke test
```

## Usage

To add PLogger to your application:

```csharp
loggerFactory.AddPLogger(Configuration);
```

In:

```csharp
public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILoggerFactory loggerFactory)
```

Or, in a generic-host / minimal-API app, on `ILoggingBuilder`:

```csharp
builder.Logging.AddPLogger();
```

Sample appsettings.json
```
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information"
    },
    "PLogger": {
      "PLogType": "file",
      "PLogEnabled": true,
      "BaseNameFile": "plogger.txt",
      "PLogFileMaxSizeKB": 1024,
      "PLogFileMessageTemplate": "{0}",
      "StringConnection": "StringConnection",
      "StoredProcedureName": "AddError",
      "MessageParameterName": "@ErrorName"
    }
  },
  "AllowedHosts": "*"
}
```

| Parameter | Description | Value
| --- | --- | --- |
| PLogType | The type of logger you are initiating | file or database |
| PLogEnabled | Enable logging | true or false
| BaseNameFile | The name of the file to log to | applog.txt
| PLogFileMaxSizeKB | The maximum size of a log file before starting a new file | 1024 |
| PLogFileMessageTemplate | A String.Format message template | "Loge Entry: {0}" |
| StringConnection | The string connection to your logging database | "Server=myServerAddress;Database=myDataBase;User Id=myUsername;Password=myPassword;" |
| StoredProcedureName | The Sproc to use when logging to database | "SprocName" |
| MessageParameterName | The Sproc Paramter Name | "@MyParamter" |
| PLogDeleteFilesOlderThanDays | Delete old log files | 0 to not delete. Otherwise number of days |
| PLogStopLoggingIfSpaceSmallerThanMB | Stop if disk is full | 0 if dont stop. Otherwise a long|
