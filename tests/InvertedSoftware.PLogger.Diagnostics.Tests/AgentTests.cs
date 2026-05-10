// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using InvertedSoftware.PLogger.Core;
using InvertedSoftware.PLogger.Core.Fingerprinting;
using InvertedSoftware.PLogger.Diagnostics;
using InvertedSoftware.PLogger.Diagnostics.AI;
using InvertedSoftware.PLogger.Diagnostics.Agents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Diagnostics.Tests;

public class AgentTests
{
    [Fact]
    public async Task TriageAgent_ParsesCannedJsonOutput()
    {
        var chat = new FakeChatClient("""
            {"category":"NullDeref","deep_analysis":true,"reasoning":"NRE on .Length"}
            """);
        var agent = new TriageAgent(chat, new TestMonitor(new PLoggerAgentsOptions()), NullLogger<TriageAgent>.Instance);

        var ctx = MakeContext(SyntheticException.Create());
        var result = await agent.RunAsync(ctx, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ErrorCategory.NullDeref, result.Category);
        Assert.True(result.DeepAnalysisWarranted);
        Assert.Equal(1, chat.CallCount);
    }

    [Fact]
    public async Task TriageAgent_OperationCanceled_ShortCircuits()
    {
        var chat = new FakeChatClient(); // no canned response — should never be called
        var agent = new TriageAgent(chat, new TestMonitor(new PLoggerAgentsOptions()), NullLogger<TriageAgent>.Instance);

        var ctx = MakeContext(new OperationCanceledException("user cancelled"));
        var result = await agent.RunAsync(ctx, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ErrorCategory.Transient, result.Category);
        Assert.False(result.DeepAnalysisWarranted);
        Assert.Equal(0, chat.CallCount);
    }

    [Fact]
    public async Task DiagnosisAgent_ParsesHypothesisFromCannedJson()
    {
        var chat = new FakeChatClient("""
            {"category":"Logic","explanation":"off-by-one","contributing_factors":["loop bound"]}
            """);
        var agent = new DiagnosisAgent(chat, new TestMonitor(new PLoggerAgentsOptions()), NullLogger<DiagnosisAgent>.Instance);

        var ctx = MakeContext(SyntheticException.Create());
        var result = await agent.RunAsync(new EnrichedContext(ctx, ""), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ErrorCategory.Logic, result.Category);
        Assert.Equal("off-by-one", result.Explanation);
        Assert.Single(result.ContributingFactors);
    }

    [Fact]
    public void ValidatorAgent_ValidUnifiedDiff_PassesScopeCheck()
    {
        var fix = new ProposedFix(
            FilePath: "src/MyApp/Foo.cs",
            UnifiedDiff: """
                --- a/src/MyApp/Foo.cs
                +++ b/src/MyApp/Foo.cs
                @@ -10,3 +10,3 @@
                -    if (x == null) throw;
                +    if (x is null) return;
                """,
            Rationale: "use null check",
            Risk: RiskLevel.Low,
            RegressionConcerns: new List<string>());

        var verdict = ValidatorAgent.Validate(fix, "src/MyApp/Foo.cs");
        Assert.True(verdict.IsValid, verdict.Reason);
    }

    [Fact]
    public void ValidatorAgent_DiffTouchingMultipleFiles_Rejected()
    {
        var fix = new ProposedFix(
            FilePath: "src/MyApp/Foo.cs",
            UnifiedDiff: """
                --- a/src/MyApp/Foo.cs
                +++ b/src/MyApp/Foo.cs
                @@ -10,1 +10,1 @@
                -bad
                +good
                --- a/src/MyApp/Bar.cs
                +++ b/src/MyApp/Bar.cs
                @@ -20,1 +20,1 @@
                -bad
                +good
                """,
            Rationale: "...",
            Risk: RiskLevel.High,
            RegressionConcerns: new List<string>());

        var verdict = ValidatorAgent.Validate(fix, "src/MyApp/Foo.cs");
        Assert.False(verdict.IsValid);
    }

    [Fact]
    public void ValidatorAgent_DiffMissingHeaders_Rejected()
    {
        var fix = new ProposedFix(
            FilePath: "src/MyApp/Foo.cs",
            UnifiedDiff: "no headers, no hunks, just text",
            Rationale: "...",
            Risk: RiskLevel.High,
            RegressionConcerns: new List<string>());

        var verdict = ValidatorAgent.Validate(fix, "src/MyApp/Foo.cs");
        Assert.False(verdict.IsValid);
    }

    [Fact]
    public async Task Orchestrator_ShortCircuits_OnTransientTriage()
    {
        // Triage replies non-deep — Diagnosis / Fix should never be called.
        var chat = new FakeChatClient(
            """{"category":"Transient","deep_analysis":false,"reasoning":"http 503 retry"}""");

        var (orch, ctx) = BuildOrchestrator(chat);
        var result = await orch.DiagnoseAsync(ctx, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ErrorCategory.Transient, result.Hypothesis.Category);
        Assert.Null(result.Fix);
        // Only triage was called.
        Assert.Equal(1, chat.CallCount);
    }

    [Fact]
    public async Task Orchestrator_FullPipeline_ProducesValidatedFix()
    {
        var triageJson = """{"category":"NullDeref","deep_analysis":true,"reasoning":"NRE"}""";
        var diagnosisJson = """{"category":"NullDeref","explanation":"x was null","contributing_factors":["unchecked input"]}""";
        var fixJson = """
            {
              "file_path":"src/MyApp/Foo.cs",
              "unified_diff":"--- a/src/MyApp/Foo.cs\n+++ b/src/MyApp/Foo.cs\n@@ -10,3 +10,3 @@\n-    if (x == null) throw;\n+    if (x is null) return;\n",
              "rationale":"prevent NRE",
              "risk":"Low",
              "regression_concerns":[]
            }
            """;
        var chat = new FakeChatClient(triageJson, diagnosisJson, fixJson);

        // Provide a SourceSnippet so the FixAgent has an anchor.
        var (orch, _) = BuildOrchestrator(chat);
        var ctx = MakeContextWithSource(SyntheticException.Create(), "src/MyApp/Foo.cs", 11);
        var result = await orch.DiagnoseAsync(ctx, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ErrorCategory.NullDeref, result.Hypothesis.Category);
        Assert.NotNull(result.Fix);
        Assert.Equal(RiskLevel.Low, result.Fix!.Risk);
        Assert.Equal(3, chat.CallCount); // triage + diagnosis + fix
    }

    private static (MultiAgentDiagnosisAgent orch, DiagnosticContext ctx) BuildOrchestrator(FakeChatClient chat)
    {
        var opts = new TestMonitor(new PLoggerAgentsOptions { FixRetryCount = 0 });
        var triage = new TriageAgent(chat, opts, NullLogger<TriageAgent>.Instance);
        var ctxAgent = new ContextAgent();
        var diagnosis = new DiagnosisAgent(chat, opts, NullLogger<DiagnosisAgent>.Instance);
        var fix = new FixAgent(chat, opts, NullLogger<FixAgent>.Instance);
        var validator = new ValidatorAgent();
        var budget = new TokenBudgetEnforcer(new AiTestMonitor(new PLoggerAiOptions { TokenBudgetPerMinute = 0 }));
        var orch = new MultiAgentDiagnosisAgent(triage, ctxAgent, diagnosis, fix, validator, opts, budget,
            NullLogger<MultiAgentDiagnosisAgent>.Instance);
        return (orch, MakeContext(SyntheticException.Create()));
    }

    private static DiagnosticContext MakeContext(Exception ex)
    {
        var hash = StackFingerprinter.Compute(ex);
        var normalized = StackFingerprinter.Normalize(ex);
        var evt = new LogEvent(DateTimeOffset.UtcNow, LogLevel.Error, "cat", new EventId(1), "msg", ex,
            Array.Empty<KeyValuePair<string, object?>>(), null, null);
        return new DiagnosticContext(evt, new ExceptionFingerprint(hash, normalized),
            Source: null, Array.Empty<LogEvent>(), Array.Empty<DiagnosticRecord>());
    }

    private static DiagnosticContext MakeContextWithSource(Exception ex, string file, int line)
    {
        var ctx = MakeContext(ex);
        return ctx with { Source = new SourceSnippet(file, line, "  10:    if (x == null) throw;\n→ 11: ...", null, null) };
    }

    private sealed class TestMonitor : IOptionsMonitor<PLoggerAgentsOptions>
    {
        public TestMonitor(PLoggerAgentsOptions value) { CurrentValue = value; }
        public PLoggerAgentsOptions CurrentValue { get; }
        public PLoggerAgentsOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<PLoggerAgentsOptions, string?> listener) => null;
    }

    private sealed class AiTestMonitor : IOptionsMonitor<PLoggerAiOptions>
    {
        public AiTestMonitor(PLoggerAiOptions value) { CurrentValue = value; }
        public PLoggerAiOptions CurrentValue { get; }
        public PLoggerAiOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<PLoggerAiOptions, string?> listener) => null;
    }
}
