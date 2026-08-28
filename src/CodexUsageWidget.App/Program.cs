using System.IO;
using CodexUsageWidget.App.Services;

namespace CodexUsageWidget.App;

/// <summary>
/// Explicit process entry point. Handles <c>--version</c> before constructing
/// the WPF shell so release verification can query the version headlessly.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 1 && string.Equals(args[0], "--version", StringComparison.Ordinal))
        {
            using Stream stdout = Console.OpenStandardOutput();
            using StreamWriter writer = new(stdout) { AutoFlush = true };
            writer.WriteLine(ApplicationVersion.Current);
            return 0;
        }

        App app = new();
        app.InitializeComponent();
        return app.Run();
    }
}
