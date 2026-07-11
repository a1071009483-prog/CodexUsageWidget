using System.Security.Cryptography;
using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Infrastructure.Security;

/// <summary>
/// Persists a single per-installation salt used by <see cref="AccountNamespaceHasher"/>.
/// On first access a 32-byte cryptographically random salt is generated, protected via
/// <see cref="IProtectedData"/> (DPAPI in production), and written to <c>salt.bin</c> in
/// the injected directory. Subsequent loads read, unprotect, and reuse it — the salt is
/// never regenerated. If the protected file is corrupt or cannot be unprotected, the
/// store fails closed (throws) rather than silently producing a new salt that would
/// orphan existing <c>account_namespaces</c> rows.
/// </summary>
public sealed class ProtectedSaltStore
{
    private const int SaltSizeBytes = 32;
    private const string SaltFileName = "salt.bin";

    private readonly string _directoryPath;
    private readonly string _saltPath;
    private readonly IProtectedData _protectedData;

    public ProtectedSaltStore(string directoryPath, IProtectedData protectedData)
    {
        ArgumentNullException.ThrowIfNull(directoryPath);
        ArgumentNullException.ThrowIfNull(protectedData);

        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new ArgumentException(
                "Directory path must not be empty or whitespace.", nameof(directoryPath));
        }

        _directoryPath = directoryPath;
        _saltPath = Path.Combine(directoryPath, SaltFileName);
        _protectedData = protectedData;
    }

    /// <summary>
    /// Returns the persisted salt, creating and persisting one on first access. The salt
    /// is reused on every subsequent load. Thread-safe via a process-local lock; the
    /// file is written atomically via a temp file rename to avoid torn writes.
    /// </summary>
    public async Task<byte[]> GetOrCreateSaltAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(_directoryPath);

        byte[]? existing = await TryReadProtectedSaltAsync(cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        byte[] protectedSalt = _protectedData.Protect(salt);
        await WriteAtomicallyAsync(protectedSalt, cancellationToken);
        return salt;
    }

    private async Task<byte[]?> TryReadProtectedSaltAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_saltPath))
        {
            return null;
        }

        byte[] protectedSalt = await File.ReadAllBytesAsync(_saltPath, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            return _protectedData.Unprotect(protectedSalt);
        }
        catch (CryptographicException)
        {
            // Fail closed: a corrupt or unprotectable salt file must not silently
            // regenerate the salt, because a new salt would invalidate every existing
            // account_namespaces row.
            throw new InvalidOperationException(
                "The protected salt file could not be unprotected. Refusing to "
                + "silently regenerate a new salt and orphan existing account "
                + "namespace rows. Delete salt.bin manually to reinitialize.");
        }
    }

    private async Task WriteAtomicallyAsync(byte[] protectedSalt, CancellationToken cancellationToken)
    {
        string tempPath = _saltPath + ".tmp";
        await File.WriteAllBytesAsync(tempPath, protectedSalt, cancellationToken)
            .ConfigureAwait(false);
        File.Move(tempPath, _saltPath, overwrite: true);
    }
}
