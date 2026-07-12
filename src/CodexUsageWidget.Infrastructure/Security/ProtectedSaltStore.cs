using System.Security.Cryptography;
using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Infrastructure.Security;

/// <summary>
/// Produces a single per-installation salt used by <see cref="AccountNamespaceHasher"/>.
/// </summary>
public interface ISaltStore
{
    /// <summary>
    /// Returns the persisted salt, creating and persisting one on first access. The salt
    /// is reused on every subsequent load and is never regenerated.
    /// </summary>
    Task<byte[]> GetOrCreateSaltAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Persists a single per-installation salt used by <see cref="AccountNamespaceHasher"/>.
/// On first access a 32-byte cryptographically random salt is generated, protected via
/// <see cref="IProtectedData"/> (DPAPI in production), and written to <c>salt.bin</c> in
/// the injected directory. Subsequent loads read, unprotect, and reuse it — the salt is
/// never regenerated. If the protected file is corrupt or cannot be unprotected, the
/// store fails closed (throws) rather than silently producing a new salt that would
/// orphan existing <c>account_namespaces</c> rows.
/// </summary>
/// <remarks>
/// <para>
/// Concurrency: in-process concurrent first-callers are serialized by an internal
/// semaphore with a double-check so only one salt is generated per process. Cross-process
/// concurrent first-access is not guarded here; it is guarded by the application's
/// single-instance mutex (OpenSpec 3.6). The file is written atomically via a uniquely
/// named temp file renamed into place to avoid torn writes.
/// </para>
/// </remarks>
public sealed class ProtectedSaltStore : ISaltStore, IDisposable
{
    private const int SaltSizeBytes = 32;
    private const string SaltFileName = "salt.bin";

    private readonly string _directoryPath;
    private readonly string _saltPath;
    private readonly IProtectedData _protectedData;
    private readonly SemaphoreSlim _createGate = new(1, 1);

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

    /// <inheritdoc />
    public async Task<byte[]> GetOrCreateSaltAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(_directoryPath);

        // Fast path: the salt already exists.
        byte[]? existing = await TryReadProtectedSaltAsync(cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        // Serialize in-process creation so two concurrent first-callers cannot each
        // generate a different salt and clobber one another.
        await _createGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-check inside the gate: another caller may have created the salt.
            existing = await TryReadProtectedSaltAsync(cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                return existing;
            }

            byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
            byte[] protectedSalt = _protectedData.Protect(salt);
            await WriteAtomicallyAsync(protectedSalt, cancellationToken)
                .ConfigureAwait(false);
            return salt;
        }
        finally
        {
            _createGate.Release();
        }
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
        // Use a uniquely named temp file so concurrent writers (or a stale temp from a
        // previous crash) cannot collide on a fixed temp path and move the wrong bytes.
        string tempPath = _saltPath + "." + Path.GetRandomFileName() + ".tmp";
        await File.WriteAllBytesAsync(tempPath, protectedSalt, cancellationToken)
            .ConfigureAwait(false);
        File.Move(tempPath, _saltPath, overwrite: true);
    }

    public void Dispose()
    {
        _createGate.Dispose();
    }
}
