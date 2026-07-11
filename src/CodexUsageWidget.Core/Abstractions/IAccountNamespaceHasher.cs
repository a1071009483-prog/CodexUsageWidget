namespace CodexUsageWidget.Core.Abstractions;

/// <summary>
/// Produces a stable, opaque namespace hash for an <see cref="AccountIdentity"/>. The
/// same account and context must yield the same hash across instances and restarts;
/// the hash must not allow recovery of the raw email. This boundary performs no model
/// consumption (<c>thread/start</c>/<c>turn/start</c>) — it is pure local hashing.
/// </summary>
public interface IAccountNamespaceHasher
{
    /// <summary>
    /// Returns the stable namespace hash for <paramref name="identity"/>.
    /// </summary>
    ValueTask<string> GetNamespaceHashAsync(
        AccountIdentity identity,
        CancellationToken cancellationToken);
}
