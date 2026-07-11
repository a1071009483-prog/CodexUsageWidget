namespace CodexUsageWidget.Core.Abstractions;

/// <summary>
/// Protects and unprotects byte payloads with current-user semantics (e.g. DPAPI
/// <see cref="System.Security.Cryptography.DataProtectionScope.CurrentUser"/>). The
/// abstraction isolates salt-protection logic from platform DPAPI availability, so the
/// hashing pipeline is fully testable on WSL/Linux.
/// </summary>
public interface IProtectedData
{
    /// <summary>
    /// Encrypts <paramref name="plaintext"/> under the current user's scope.
    /// </summary>
    byte[] Protect(byte[] plaintext);

    /// <summary>
    /// Decrypts <paramref name="encrypted"/> previously produced by <see cref="Protect"/>.
    /// </summary>
    byte[] Unprotect(byte[] encrypted);
}
