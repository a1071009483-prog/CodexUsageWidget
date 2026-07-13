namespace CodexUsageWidget.Core.Abstractions;

/// <summary>
/// Resolves the current ChatGPT account identity from the live App Server.
/// The returned <see cref="AccountIdentity"/> must be used only for namespace
/// hashing and must never be persisted or displayed.
/// </summary>
public interface IAccountIdentityProvider
{
    /// <summary>
    /// Reads the authenticated account and returns a stable identity for the
    /// current session. Throws if authentication is missing or unsupported.
    /// </summary>
    Task<AccountIdentity> GetIdentityAsync(CancellationToken cancellationToken = default);
}
