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
public sealed class AccountNamespaceHasher : IAccountNamespaceHasher
{
    /// <summary>
    /// Stable constant substituted for an absent workspace scope.
    /// </summary>
    public const string GlobalWorkspaceScope = "global";

    private readonly ProtectedSaltStore _saltStore;
    private byte[]? _cachedSalt;

    public AccountNamespaceHasher(ProtectedSaltStore saltStore)
    {
        ArgumentNullException.ThrowIfNull(saltStore);
        _saltStore = saltStore;
    }

    /// <inheritdoc />
    public async ValueTask<string> GetNamespaceHashAsync(
        AccountIdentity identity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);

        _cachedSalt ??= await _saltStore.GetOrCreateSaltAsync(cancellationToken)
            .ConfigureAwait(false);

        return ComputeHash(_cachedSalt, identity);
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
