namespace CodexUsageWidget.Infrastructure.Persistence;

/// <summary>
/// DDL constants and migration definitions for the durable usage-state SQLite database.
///
/// This schema stores only non-sensitive metadata: account namespace hashes (never raw
/// email or account identifiers), quota field snapshots, model IDs, timestamps, outcome
/// categories, and redacted notification payloads. It excludes tokens, cookies, raw
/// credentials, prompt-response bodies, and workspace content.
/// </summary>
public static class UsageStateSchema
{
    /// <summary>
    /// The highest migration version known to this build.
    /// </summary>
    public const int LatestVersion = 1;

    /// <summary>
    /// Ordered list of all known migrations. New migrations are appended here.
    /// </summary>
    public static readonly IReadOnlyList<Migration> Migrations =
    [
        new(1, "initial_schema", InitialSchemaSql),
    ];

    /// <summary>
    /// Version 1: creates all usage-state tables. Idempotent via IF NOT EXISTS so that
    /// re-running after an interrupted PRAGMA update is safe.
    /// </summary>
    private const string InitialSchemaSql = """
CREATE TABLE IF NOT EXISTS settings (
    key TEXT PRIMARY KEY,
    value TEXT,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS account_namespaces (
    namespace_hash TEXT PRIMARY KEY,
    plan_type TEXT,
    created_at TEXT NOT NULL,
    last_seen_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS activation_attempts (
    attempt_id TEXT PRIMARY KEY,
    namespace_hash TEXT NOT NULL,
    workspace_scope TEXT NOT NULL,
    window_key TEXT NOT NULL,
    window_kind TEXT NOT NULL,
    suppression_deadline TEXT NOT NULL,
    observed_at TEXT NOT NULL,
    attempt_at TEXT NOT NULL,
    pre_used_percent INTEGER NOT NULL,
    pre_resets_at TEXT,
    model_id TEXT,
    turn_started INTEGER NOT NULL DEFAULT 0,
    terminal_outcome TEXT,
    post_used_percent INTEGER,
    post_resets_at TEXT,
    cleanup_state TEXT NOT NULL DEFAULT 'none',
    UNIQUE(namespace_hash, workspace_scope, window_key)
);

CREATE TABLE IF NOT EXISTS notifications (
    sequence INTEGER PRIMARY KEY AUTOINCREMENT,
    namespace_hash TEXT NOT NULL,
    method TEXT NOT NULL,
    received_at TEXT NOT NULL,
    redacted_payload TEXT
);

CREATE TABLE IF NOT EXISTS cleanup_work (
    cleanup_id TEXT PRIMARY KEY,
    attempt_id TEXT NOT NULL,
    thread_id TEXT NOT NULL,
    enqueued_at TEXT NOT NULL,
    state TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS audit_rows (
    audit_id TEXT PRIMARY KEY,
    attempt_id TEXT,
    namespace_hash TEXT NOT NULL,
    model_id TEXT,
    observed_at TEXT NOT NULL,
    pre_quota TEXT,
    post_quota TEXT,
    resets_at TEXT,
    turn_crossed_boundary INTEGER NOT NULL DEFAULT 0,
    outcome TEXT,
    error_category TEXT,
    recorded_at TEXT NOT NULL
);
""";
}
