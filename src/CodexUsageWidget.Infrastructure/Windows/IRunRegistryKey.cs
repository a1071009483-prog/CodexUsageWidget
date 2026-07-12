namespace CodexUsageWidget.Infrastructure.Windows;

/// <summary>
/// Abstraction over the current user's Windows Run registry key.
/// </summary>
public interface IRunRegistryKey
{
    /// <summary>Reads a string value by name, or <c>null</c> if absent.</summary>
    string? GetValue(string name);

    /// <summary>Writes a string value by name.</summary>
    void SetValue(string name, string value);

    /// <summary>Deletes a value by name if it exists.</summary>
    void DeleteValue(string name);
}
