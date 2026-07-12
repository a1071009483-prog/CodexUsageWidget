namespace CodexUsageWidget.Core.Abstractions;

/// <summary>
/// Read-only validator that loads and checks the durable safety state before any
/// automatic activation is permitted. The validator opens the usage-state database
/// (which runs pending migrations — a migration failure fails closed), then
/// verifies structural integrity, schema version, required-table presence, and at
/// least one row invariant.
///
/// Contract:
/// <list type="bullet">
/// <item>It performs NO model consumption (<c>thread/start</c>/<c>turn/start</c>) —
/// it is a pure read-only check.</item>
/// <item>It persists NO credentials, raw email, or sensitive payloads — it reads
/// only non-sensitive schema metadata and invariant counts.</item>
/// <item>It NEVER silently rebuilds or repairs state. Any validation failure
/// returns an invalid result so the caller disables activation; the validator does
/// not recreate tables, rewrite rows, or re-apply migrations beyond what
/// <see cref="Persistence.UsageStateDatabase"/> does on a normal connection open.
/// </item>
/// <item>Any exception during open/migration/read is caught and returned as an
/// invalid result (fail-closed); the validator does not throw to its caller.
/// </item>
/// </list>
/// See design.md decision 4 + risks: "State corruption could erase a live guard →
/// disable activation rather than recreate state automatically."
/// </summary>
public interface ISafetyStateValidator
{
    /// <summary>
    /// Loads and validates the durable safety state. Returns a valid result only
    /// when the database opens, migrates to the latest known schema version, passes
    /// <c>PRAGMA integrity_check</c>, contains all required tables, and no row
    /// invariant is violated. Any failure returns an invalid result categorizing
    /// the failure; the call does not throw for database/IO errors.
    /// </summary>
    Task<SafetyStateValidationResult> ValidateAsync(CancellationToken cancellationToken);
}
