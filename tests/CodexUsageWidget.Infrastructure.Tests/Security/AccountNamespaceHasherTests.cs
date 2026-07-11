using System.Security.Cryptography;
using System.Text;
using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Infrastructure.Persistence;
using CodexUsageWidget.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CodexUsageWidget.Infrastructure.Tests.Security;

public sealed class AccountNamespaceHasherTests
{
    private static readonly byte[] FixedSalt = RandomNumberGenerator.GetBytes(32);

    [Fact]
    public async Task SameIdentityProducesSameHashAcrossCalls()
    {
        var hasher = new AccountNamespaceHasher(new ProtectedSaltStore(
            TempDir(), new IdentityProtectedData()));

        var identity = new AccountIdentity("alice@example.com", "plus", "global");
        string first = await hasher.GetNamespaceHashAsync(identity, CancellationToken.None);
        string second = await hasher.GetNamespaceHashAsync(identity, CancellationToken.None);

        Assert.Equal(first, second);
    }

    [Fact]
    public void SameIdentityProducesSameHashWithFixedSalt()
    {
        var identity = new AccountIdentity("alice@example.com", "plus", "global");
        string first = AccountNamespaceHasher.ComputeHash(FixedSalt, identity);
        string second = AccountNamespaceHasher.ComputeHash(FixedSalt, identity);

        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentEmailsProduceDifferentHashes()
    {
        var a = AccountNamespaceHasher.ComputeHash(FixedSalt, new AccountIdentity("alice@example.com", "plus", "global"));
        var b = AccountNamespaceHasher.ComputeHash(FixedSalt, new AccountIdentity("bob@example.com", "plus", "global"));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void DifferentPlansProduceDifferentHashes()
    {
        var a = AccountNamespaceHasher.ComputeHash(FixedSalt, new AccountIdentity("alice@example.com", "free", "global"));
        var b = AccountNamespaceHasher.ComputeHash(FixedSalt, new AccountIdentity("alice@example.com", "plus", "global"));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void DifferentWorkspaceScopesProduceDifferentHashes()
    {
        var a = AccountNamespaceHasher.ComputeHash(FixedSalt, new AccountIdentity("alice@example.com", "plus", "global"));
        var b = AccountNamespaceHasher.ComputeHash(FixedSalt, new AccountIdentity("alice@example.com", "plus", "team-42"));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void NormalizationIsCaseAndWhitespaceInsensitive()
    {
        var a = AccountNamespaceHasher.ComputeHash(FixedSalt, new AccountIdentity("alice@example.com", "plus", "global"));
        var b = AccountNamespaceHasher.ComputeHash(FixedSalt, new AccountIdentity("  ALICE@example.com  ", "PLUS", "GLOBAL"));
        var c = AccountNamespaceHasher.ComputeHash(FixedSalt, new AccountIdentity("Alice@Example.com", "Plus", "Global"));

        Assert.Equal(a, b);
        Assert.Equal(a, c);
    }

    [Fact]
    public void MissingWorkspaceScopeDefaultsToGlobalStably()
    {
        var explicitGlobal = AccountNamespaceHasher.ComputeHash(FixedSalt, new AccountIdentity("alice@example.com", "plus", "global"));
        var nullScope = AccountNamespaceHasher.ComputeHash(FixedSalt, new AccountIdentity("alice@example.com", "plus", null));
        var emptyScope = AccountNamespaceHasher.ComputeHash(FixedSalt, new AccountIdentity("alice@example.com", "plus", "  "));
        var whitespaceScope = AccountNamespaceHasher.ComputeHash(FixedSalt, new AccountIdentity("alice@example.com", "plus", ""));

        Assert.Equal(explicitGlobal, nullScope);
        Assert.Equal(explicitGlobal, emptyScope);
        Assert.Equal(explicitGlobal, whitespaceScope);
    }

    [Fact]
    public void NullPlanAndEmptyPlanAreEquivalent()
    {
        var nullPlan = AccountNamespaceHasher.ComputeHash(FixedSalt, new AccountIdentity("alice@example.com", null, "global"));
        var emptyPlan = AccountNamespaceHasher.ComputeHash(FixedSalt, new AccountIdentity("alice@example.com", "", "global"));
        var whitespacePlan = AccountNamespaceHasher.ComputeHash(FixedSalt, new AccountIdentity("alice@example.com", "  ", "global"));

        Assert.Equal(nullPlan, emptyPlan);
        Assert.Equal(nullPlan, whitespacePlan);
    }

    [Fact]
    public void HashDoesNotContainRawEmailSubstring()
    {
        string email = "alice.lewis.the.first@example.com";
        string hash = AccountNamespaceHasher.ComputeHash(FixedSalt, new AccountIdentity(email, "plus", "global"));

        Assert.DoesNotContain(email, hash, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alice", hash, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("example.com", hash, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaltPersistsAcrossInstancesProducingSameHash()
    {
        string dir = TempDir();
        var identity = new AccountIdentity("carol@example.com", "plus", "global");

        var hasherA = new AccountNamespaceHasher(new ProtectedSaltStore(dir, new IdentityProtectedData()));
        string first = await hasherA.GetNamespaceHashAsync(identity, CancellationToken.None);

        var hasherB = new AccountNamespaceHasher(new ProtectedSaltStore(dir, new IdentityProtectedData()));
        string second = await hasherB.GetNamespaceHashAsync(identity, CancellationToken.None);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task SaltIsReusedNotRegeneratedOnSubsequentLoad()
    {
        string dir = TempDir();
        var spy = new CountingProtectedData();

        var hasherA = new AccountNamespaceHasher(new ProtectedSaltStore(dir, spy));
        string firstHash = await hasherA.GetNamespaceHashAsync(
            new AccountIdentity("dave@example.com", "plus", "global"), CancellationToken.None);

        int protectCallsAfterFirst = spy.ProtectCallCount;

        var hasherB = new AccountNamespaceHasher(new ProtectedSaltStore(dir, new CountingProtectedData()));
        string secondHash = await hasherB.GetNamespaceHashAsync(
            new AccountIdentity("dave@example.com", "plus", "global"), CancellationToken.None);

        Assert.Equal(firstHash, secondHash);
        Assert.Equal(1, protectCallsAfterFirst);
    }

    [Fact]
    public async Task NamespaceHashWrittenToDatabaseDoesNotPersistRawEmail()
    {
        string dbDir = TempDir();
        string email = "eve.mallory.the.long.one@example.com";
        string plan = "plus";
        string scope = "global";

        string hash;
        var db = new UsageStateDatabase(dbDir);
        await using (SqliteConnection connection = await db.CreateConnectionAsync(CancellationToken.None))
        {
            var hasher = new AccountNamespaceHasher(new ProtectedSaltStore(dbDir, new IdentityProtectedData()));
            hash = await hasher.GetNamespaceHashAsync(
                new AccountIdentity(email, plan, scope), CancellationToken.None);

            Assert.False(string.IsNullOrEmpty(hash));

            await InsertNamespaceRowAsync(connection, hash, plan, CancellationToken.None);
        }

        // Release the pooled file handle before reading raw bytes off disk.
        SqliteConnection.ClearAllPools();

        string dbPath = Path.Combine(dbDir, "state.db");
        Assert.True(File.Exists(dbPath), $"Expected database file at {dbPath}");
        byte[] dbBytes = await File.ReadAllBytesAsync(dbPath);
        string dbText = Encoding.UTF8.GetString(dbBytes);

        Assert.DoesNotContain(email, dbText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("eve.mallory", dbText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("example.com", dbText, StringComparison.OrdinalIgnoreCase);

        // Also scan table contents directly to be unambiguous about persisted rows.
        await using (SqliteConnection connection = await db.CreateConnectionAsync(CancellationToken.None))
        {
            await foreach (var row in ReadNamespaceRowsAsync(connection, CancellationToken.None))
            {
                Assert.DoesNotContain(email, row, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(hash, row, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task ProtectedSaltFileDoesNotContainRawEmail()
    {
        string dir = TempDir();
        string email = "frank.sensitive.identifier@example.com";

        var hasher = new AccountNamespaceHasher(new ProtectedSaltStore(dir, new IdentityProtectedData()));
        await hasher.GetNamespaceHashAsync(
            new AccountIdentity(email, "plus", "global"), CancellationToken.None);

        string saltPath = Path.Combine(dir, "salt.bin");
        Assert.True(File.Exists(saltPath), $"Expected salt file at {saltPath}");
        byte[] saltBytes = await File.ReadAllBytesAsync(saltPath);
        string saltText = Encoding.UTF8.GetString(saltBytes);

        Assert.DoesNotContain(email, saltText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("frank", saltText, StringComparison.OrdinalIgnoreCase);
    }

    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "codex-ns-test-" + Guid.NewGuid().ToString("N"));

    private static async Task InsertNamespaceRowAsync(
        SqliteConnection connection, string hash, string? plan, CancellationToken ct)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO account_namespaces (namespace_hash, plan_type, created_at, last_seen_at)
            VALUES (@hash, @plan, @created, @seen);
            """;
        command.Parameters.AddWithValue("@hash", hash);
        command.Parameters.AddWithValue("@plan", (object?)plan ?? DBNull.Value);
        command.Parameters.AddWithValue("@created", "2026-07-12T00:00:00Z");
        command.Parameters.AddWithValue("@seen", "2026-07-12T00:00:00Z");
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async IAsyncEnumerable<string> ReadNamespaceRowsAsync(
        SqliteConnection connection, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT namespace_hash, plan_type FROM account_namespaces;";
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            string ns = reader.GetString(0);
            string? plan = reader.IsDBNull(1) ? null : reader.GetString(1);
            yield return $"{ns}|{plan}";
        }
    }

    /// <summary>
    /// Fake <see cref="IProtectedData"/> that returns input unchanged. Suitable for WSL/Linux
    /// test environments where DPAPI throws PlatformNotSupportedException; isolates salt
    /// persistence logic from the platform protection layer.
    /// </summary>
    private sealed class IdentityProtectedData : IProtectedData
    {
        public byte[] Protect(byte[] plaintext) => plaintext;
        public byte[] Unprotect(byte[] encrypted) => encrypted;
    }

    /// <summary>
    /// Fake that counts <see cref="Protect"/> calls so tests can assert salt is generated
    /// once and reused thereafter.
    /// </summary>
    private sealed class CountingProtectedData : IProtectedData
    {
        public int ProtectCallCount;
        public byte[] Protect(byte[] plaintext)
        {
            ProtectCallCount++;
            return plaintext;
        }
        public byte[] Unprotect(byte[] encrypted) => encrypted;
    }
}
