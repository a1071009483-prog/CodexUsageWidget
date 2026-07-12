using CodexUsageWidget.Infrastructure.Windows;

namespace CodexUsageWidget.Infrastructure.Tests.Windows;

internal sealed class FakeRunRegistryKey : IRunRegistryKey
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> Values => _values;

    public string? GetValue(string name)
    {
        _values.TryGetValue(name, out string? value);
        return value;
    }

    public void SetValue(string name, string value) => _values[name] = value;

    public void DeleteValue(string name) => _values.Remove(name);
}
