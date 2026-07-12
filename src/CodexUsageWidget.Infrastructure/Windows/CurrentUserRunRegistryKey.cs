using Microsoft.Win32;

namespace CodexUsageWidget.Infrastructure.Windows;

/// <summary>
/// Wrapper around <c>HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run</c>.
/// </summary>
public sealed class CurrentUserRunRegistryKey : IRunRegistryKey, IDisposable
{
    private readonly string _appName;
    private readonly RegistryKey _key;

    public CurrentUserRunRegistryKey(string appName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appName);

        _appName = appName;
        _key = Registry.CurrentUser.CreateSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            writable: true);
    }

    public string? GetValue(string name) => _key.GetValue(name) as string;

    public void SetValue(string name, string value) => _key.SetValue(name, value, RegistryValueKind.String);

    public void DeleteValue(string name)
    {
        if (_key.GetValue(name) is not null)
        {
            _key.DeleteValue(name, throwOnMissingValue: false);
        }
    }

    public void Dispose() => _key.Dispose();
}
