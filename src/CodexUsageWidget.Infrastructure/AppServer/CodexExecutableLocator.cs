namespace CodexUsageWidget.Infrastructure.AppServer;

public sealed record CodexExecutableResolution(
    bool Found,
    string? Command,
    string Source,
    string Diagnostic);

public sealed class CodexExecutableLocator
{
    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, string?> _resolveOnPath;
    private readonly Func<string?> _resolveDesktopInstallation;

    public CodexExecutableLocator(
        Func<string, string?> getEnvironmentVariable,
        Func<string, bool> fileExists,
        Func<string, string?> resolveOnPath,
        Func<string?>? resolveDesktopInstallation = null)
    {
        _getEnvironmentVariable = getEnvironmentVariable
            ?? throw new ArgumentNullException(nameof(getEnvironmentVariable));
        _fileExists = fileExists ?? throw new ArgumentNullException(nameof(fileExists));
        _resolveOnPath = resolveOnPath ?? throw new ArgumentNullException(nameof(resolveOnPath));
        _resolveDesktopInstallation = resolveDesktopInstallation ?? (() => null);
    }

    public CodexExecutableResolution Locate(string? configuredPath = null)
    {
        if (IsAccessible(configuredPath))
        {
            return Found(configuredPath!, "configured");
        }

        string? environmentPath = _getEnvironmentVariable("CODEX_EXECUTABLE");
        if (IsAccessible(environmentPath))
        {
            return Found(environmentPath!, "environment");
        }

        string? desktopInstallation = _resolveDesktopInstallation();
        if (IsAccessible(desktopInstallation))
        {
            return Found(desktopInstallation!, "desktop-installation");
        }

        string? pathResolution = _resolveOnPath("codex");
        if (IsAccessible(pathResolution))
        {
            return Found(pathResolution!, "path");
        }

        return new CodexExecutableResolution(
            false,
            null,
            "unavailable",
            "The Codex executable was not found or was inaccessible.");
    }

    public static CodexExecutableLocator CreateSystem() =>
        new(
            Environment.GetEnvironmentVariable,
            File.Exists,
            ResolveOnSystemPath,
            ResolveDesktopInstallation);

    private bool IsAccessible(string? candidate) =>
        !string.IsNullOrWhiteSpace(candidate) && _fileExists(candidate);

    private static CodexExecutableResolution Found(string command, string source) =>
        new(
            true,
            command,
            source,
            $"The Codex executable was resolved from {source} configuration.");

    private static string? ResolveOnSystemPath(string command)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string? pathExtensions = Environment.GetEnvironmentVariable("PATHEXT");
        string[] extensions = string.IsNullOrWhiteSpace(pathExtensions)
            ? [".exe", ".cmd", ".bat", ".com"]
            : pathExtensions.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        IEnumerable<string> executableNames = Path.HasExtension(command)
            ? [command]
            : extensions.Select(extension => command + extension);

        foreach (string segment in path.Split(Path.PathSeparator))
        {
            string directory = segment.Trim().Trim('"');
            if (directory.Length == 0)
            {
                continue;
            }

            foreach (string executableName in executableNames)
            {
                try
                {
                    string candidate = Path.Combine(directory, executableName);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch (ArgumentException)
                {
                }
                catch (NotSupportedException)
                {
                }
            }
        }

        return null;
    }

    private static string? ResolveDesktopInstallation()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            return null;
        }

        string binDirectory = Path.Combine(localAppData, "OpenAI", "Codex", "bin");
        try
        {
            if (!Directory.Exists(binDirectory))
            {
                return null;
            }

            return Directory.EnumerateDirectories(binDirectory)
                .Select(directory => Path.Combine(directory, "codex.exe"))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
