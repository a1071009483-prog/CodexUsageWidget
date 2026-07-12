namespace CodexUsageWidget.Core.Abstractions;

/// <summary>
/// Categorizes the kind of safety-state validation failure that caused activation
/// to be disabled. <see cref="None"/> accompanies a valid result; all other values
/// are fail-closed conditions under which automatic triggering MUST stay disabled
/// until the underlying state is explicitly repaired. The validator never silently
/// rebuilds state — it only reports the failure so the caller can disable
/// activation (see design.md decision 4 + risks: "State corruption could erase a
/// live guard → disable activation rather than recreate state automatically").
/// </summary>
public enum SafetyStateFailureKind
{
    /// <summary>
    /// No failure; the durable safety state validated successfully and activation
    /// may proceed subject to the remaining preconditions.
    /// </summary>
    None = 0,

    /// <summary>
    /// The database file is structurally damaged: <c>PRAGMA integrity_check</c>
    /// returned a non-<c>ok</c> result, or the connection could not be opened
    /// because the SQLite file is not a valid database.
    /// </summary>
    Corruption = 1,

    /// <summary>
    /// The persisted <c>user_version</c> does not match
    /// <see cref="Persistence.UsageStateSchema.LatestVersion"/> after migration ran,
    /// indicating the migration did not advance the schema to the expected version.
    /// </summary>
    MigrationMismatch = 2,

    /// <summary>
    /// A required table is missing or a persisted row violates an invariant,
    /// indicating internally inconsistent anti-repeat state.
    /// </summary>
    InconsistentRows = 3,

    /// <summary>
    /// A durable write (migration transaction or schema advance) failed, leaving
    /// the database in an indeterminate state.
    /// </summary>
    DurableWriteFailure = 4,

    /// <summary>
    /// The database file or its directory could not be read due to an I/O error
    /// or missing file, distinct from structural corruption.
    /// </summary>
    Unreadable = 5,
}

/// <summary>
/// Immutable outcome of validating the durable safety state before activation.
/// When <see cref="IsValid"/> is <c>true</c>, the schema is intact, the migration
/// version matches, all required tables are present, and no row invariant is
/// violated. When <c>false</c>, <see cref="FailureKind"/> categorizes the failure
/// and <see cref="FailureReason"/> carries a safe, non-sensitive description.
///
/// This result is the activation gate: the coordinator MUST disable automatic
/// triggering for any non-<c>None</c> <see cref="FailureKind"/> and MUST NOT
/// recover by treating damaged state as proof that no trigger has occurred.
/// </summary>
/// <param name="IsValid"><c>true</c> when all safety-state checks passed.</param>
/// <param name="FailureKind">
/// The failure category, or <see cref="SafetyStateFailureKind.None"/> when valid.
/// </param>
/// <param name="FailureReason">
/// A safe, non-sensitive human-readable description of the failure (never contains
/// account identifiers, hashes, credentials, or raw payload data). <c>null</c> when
/// valid.
/// </param>
public sealed record SafetyStateValidationResult(
    bool IsValid,
    SafetyStateFailureKind FailureKind,
    string? FailureReason)
{
    /// <summary>
    /// A valid result with no failure. Convenience factory for the success path.
    /// </summary>
    public static SafetyStateValidationResult Valid { get; } =
        new(true, SafetyStateFailureKind.None, null);

    /// <summary>
    /// Builds a fail-closed invalid result with the given kind and reason.
    /// </summary>
    public static SafetyStateValidationResult Failed(
        SafetyStateFailureKind kind, string reason) =>
        new(false, kind, reason);
}
