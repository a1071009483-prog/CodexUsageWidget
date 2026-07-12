using Microsoft.Data.Sqlite;
using System.Text;
using Xunit;

namespace CodexUsageWidget.Infrastructure.Tests.Security;

/// <summary>
/// Shared assertions for automated sensitive-data scans across logs, SQLite rows,
/// settings files, crash reports, and audit exports.
/// </summary>
internal static class SensitiveDataAsserts
{
    /// <summary>Canonical forbidden literals that no local artifact should contain.</summary>
    public static readonly string[] ForbiddenLiterals =
    [
        "sk-secret-token",
        "sk-live-abc123",
        "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9",
        "user@example.com",
        "admin@corp.invalid",
        "secret-prompt-body",
        "secret-response-body",
        "Authorization: Bearer super-secret",
        "C:\\Users\\Secret\\project",
        "/home/secret/project",
    ];

    /// <summary>
    /// Asserts that <paramref name="content"/> contains none of the forbidden literals
    /// and no common credential prefixes.
    /// </summary>
    public static void AssertContainsNoSensitiveData(string content)
    {
        Assert.False(string.IsNullOrEmpty(content), "Content must not be empty when scanning.");

        foreach (string literal in ForbiddenLiterals)
        {
            Assert.DoesNotContain(literal, content, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("sk-", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearer ", content, StringComparison.Ordinal);
    }

    /// <summary>Reads the entire SQLite database file as a UTF-8 string.</summary>
    public static string ReadDatabaseText(string databaseDirectory)
    {
        string dbPath = Path.Combine(databaseDirectory, "state.db");
        Assert.True(File.Exists(dbPath), "Expected SQLite database file to exist.");
        SqliteConnection.ClearAllPools();
        byte[] bytes = File.ReadAllBytes(dbPath);
        return Encoding.UTF8.GetString(bytes);
    }
}
