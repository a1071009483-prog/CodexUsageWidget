using System.Security.Cryptography;
using System.Text;
using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Infrastructure.Security;

/// <summary>
/// Stable account/workspace namespace hasher. Normalizes identity material (lowercase
/// + trim email/plan/workspace scope; absent workspace scope becomes the stable constant
/// "global"), then computes HMAC-SHA256 keyed by a persisted protected salt and returns
/// a base64url encoding. The hashing pipeline is platform-agnostic; only the salt
/// protection layer (<see cref="IProtectedData"/>) depends on the platform.
/// </summary>
/// <remarks>
/// Thread-safety: the salt is loaded exactly once via a <see cref="Lazy{T}"/> with
/// <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/> so concurrent
/// <see cref="GetNamespaceHashAsync"/> calls never race to generate different salts.
/// Once loaded, the salt is cached for the instance lifetime; a corrupt salt store faults
/// the cached task and re-throws on every subsequent call (fail closed, no retry).
/// </remarks>
public sealed class AccountNamespaceHasher : IAccountNamespaceHasher
{
    /// <summary>
    /// Stable constant substituted for an absent workspace scope.
    /// </summary>
    public const string GlobalWorkspaceScope = "global";

    private readonly ISaltStore _saltStore;
    private readonly Lazy<Task<byte[]>> _saltTask;

    public AccountNamespaceHasher(ISaltStore saltStore)
    {
        ArgumentNullException.ThrowIfNull(saltStore);
        _saltStore = saltStore;
        _saltTask = new Lazy<Task<byte[]>>(
            () => _saltStore.GetOrCreateSaltAsync(CancellationToken.None),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public async ValueTask<string> GetNamespaceHashAsync(
        AccountIdentity identity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        cancellationToken.ThrowIfCancellationRequested();

        // The salt load is one-time and uses CancellationToken.None (it is a fast file
        // read + DPAPI unprotect). Re-check the caller's token after the await so a
        // cancellation observed during load is honored before the (fast, synchronous)
        // hash computation.
        byte[] salt = await _saltTask.Value.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return ComputeHash(salt, identity);
    }

    /// <summary>
    /// Pure, platform-agnostic hash computation: HMAC-SHA256 over the normalized
    /// identity string, encoded as base64url. Exposed so the hashing contract can be
    /// tested without any file or DPAPI dependency.
    /// </summary>
    public static string ComputeHash(byte[] salt, AccountIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(salt);
        ArgumentNullException.ThrowIfNull(identity);

        if (salt.Length == 0)
        {
            throw new ArgumentException("Salt must not be empty.", nameof(salt));
        }

        string normalized = Normalize(identity);
        byte[] message = Encoding.UTF8.GetBytes(normalized);
        using var hmac = new HMACSHA256(salt);
        byte[] digest = hmac.ComputeHash(message);
        return ToBase64Url(digest);
    }

    private static string Normalize(AccountIdentity identity)
    {
        string email = NormalizeToken(identity.Email, fallback: string.Empty);
        if (email.Length == 0)
        {
            throw new ArgumentException(
                "AccountIdentity.Email must not be empty or whitespace.", nameof(identity));
        }

        string plan = NormalizeToken(identity.Plan, fallback: string.Empty);
        string scope = NormalizeToken(identity.WorkspaceScope, fallback: GlobalWorkspaceScope);

        return $"{email}|{plan}|{scope}";
    }

    private static string NormalizeToken(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim().ToLowerInvariant();
    }

    private static string ToBase64Url(byte[] digest)
    {
        string base64 = Convert.ToBase64String(digest);
        return base64
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
