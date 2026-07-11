using System.Security.Cryptography;
using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Infrastructure.Security;

/// <summary>
/// Production <see cref="IProtectedData"/> backed by Windows DPAPI under
/// <see cref="DataProtectionScope.CurrentUser"/>. This is a thin wrapper around
/// <see cref="ProtectedData"/>; its contract is verified through the abstraction and it
/// is not unit-tested under WSL (DPAPI throws <c>PlatformNotSupportedException</c> there).
/// </summary>
public sealed class DpapiProtectedData : IProtectedData
{
    /// <inheritdoc />
    public byte[] Protect(byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
    }

    /// <inheritdoc />
    public byte[] Unprotect(byte[] encrypted)
    {
        ArgumentNullException.ThrowIfNull(encrypted);
        return ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
    }
}
