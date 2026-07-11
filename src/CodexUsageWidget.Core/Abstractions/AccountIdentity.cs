namespace CodexUsageWidget.Core.Abstractions;

/// <summary>
/// Identity material to be hashed into a stable account/workspace namespace. The
/// <see cref="Email"/> field is SENSITIVE: it must never be emitted to diagnostics,
/// audit rows, logs, or persistent storage. Only its hash may be persisted.
/// </summary>
/// <param name="Email">
/// ChatGPT account email. SENSITIVE: never persisted raw, never written to diagnostics
/// or audit. Used only as input to the namespace hash.
/// </param>
/// <param name="Plan">
/// Optional plan type (e.g. "free", "plus"). Null/whitespace is treated as absent.
/// </param>
/// <param name="WorkspaceScope">
/// Workspace scope. Null/empty/whitespace defaults to the stable constant "global".
/// </param>
public sealed record AccountIdentity(
    string Email,
    string? Plan = null,
    string? WorkspaceScope = null);
