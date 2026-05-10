-- SPDX-License-Identifier: Apache-2.0
-- PLogger 2.0 — initial diagnostic store schema (SQL Server 2025).
-- Idempotent: safe to run on every host startup.

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'plogger')
BEGIN
    EXEC(N'CREATE SCHEMA plogger');
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'plogger.Diagnostics') AND type = N'U')
BEGIN
    CREATE TABLE plogger.Diagnostics (
        Id                BIGINT IDENTITY(1,1) NOT NULL,
        Fingerprint       BINARY(16)           NOT NULL,
        FirstSeenUtc      DATETIME2(3)         NOT NULL,
        LastSeenUtc       DATETIME2(3)         NOT NULL,
        OccurrenceCount   BIGINT               NOT NULL CONSTRAINT DF_plogger_Diagnostics_OccurrenceCount DEFAULT (1),
        AppName           NVARCHAR(200)        NOT NULL,
        Environment       NVARCHAR(50)         NOT NULL,
        ExceptionType     NVARCHAR(500)        NOT NULL,
        ExceptionMessage  NVARCHAR(MAX)        NOT NULL,
        NormalizedStack   NVARCHAR(MAX)        NOT NULL,
        SourceFile        NVARCHAR(1000)       NULL,
        SourceLine        INT                  NULL,
        Diagnosis         NVARCHAR(MAX)        NULL,        -- JSON: hypothesis + fix + confidence + model
        DiagnosisVector   VECTOR(1536)         NULL,        -- SQL 2025 native vector type, populated by Phase 3
        ResolutionState   TINYINT              NOT NULL CONSTRAINT DF_plogger_Diagnostics_ResolutionState DEFAULT (0),
        GitHubIssueUrl    NVARCHAR(500)        NULL,
        AppliedCommitSha  NVARCHAR(64)         NULL,
        CONSTRAINT PK_plogger_Diagnostics PRIMARY KEY CLUSTERED (Id)
    );

    -- Per-locked-decision (planning §"Locked-in decisions"): single DB across
    -- many apps, indexed by (App, Env, Fingerprint). Vector index shared so
    -- dedup works across apps. No per-app schemas.
    CREATE UNIQUE NONCLUSTERED INDEX UX_plogger_Diagnostics_AppEnvFingerprint
        ON plogger.Diagnostics (AppName, Environment, Fingerprint);

    CREATE NONCLUSTERED INDEX IX_plogger_Diagnostics_LastSeen
        ON plogger.Diagnostics (AppName, Environment, LastSeenUtc DESC);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'plogger.DiagnosticOccurrences') AND type = N'U')
BEGIN
    CREATE TABLE plogger.DiagnosticOccurrences (
        Id            BIGINT IDENTITY(1,1) NOT NULL,
        DiagnosticId  BIGINT               NOT NULL,
        OccurredUtc   DATETIME2(3)         NOT NULL,
        TraceId       NVARCHAR(64)         NULL,
        SpanId        NVARCHAR(32)         NULL,
        Properties    NVARCHAR(MAX)        NULL,
        CONSTRAINT PK_plogger_DiagnosticOccurrences PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_plogger_DiagnosticOccurrences_Diagnostics
            FOREIGN KEY (DiagnosticId) REFERENCES plogger.Diagnostics(Id)
    );

    CREATE NONCLUSTERED INDEX IX_plogger_DiagnosticOccurrences_Diagnostic
        ON plogger.DiagnosticOccurrences (DiagnosticId, OccurredUtc DESC);
END
GO
