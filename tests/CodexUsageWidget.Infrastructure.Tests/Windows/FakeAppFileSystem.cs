using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Infrastructure.Tests.Windows;

internal sealed class FakeAppFileSystem : IAppFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> Files => _files;

    public Task<AppFileReadResult> ReadTextAsync(
        AppFileReadRequest request,
        CancellationToken cancellationToken)
    {
        if (_files.TryGetValue(Normalize(request.Path), out string? content))
        {
            return Task.FromResult(new AppFileReadResult(true, content));
        }

        return Task.FromResult(new AppFileReadResult(false, null, "not-found"));
    }

    public Task<AppFileWriteResult> WriteTextAsync(
        AppFileWriteRequest request,
        CancellationToken cancellationToken)
    {
        _files[Normalize(request.Path)] = request.Content;
        return Task.FromResult(new AppFileWriteResult(true));
    }

    private static string Normalize(string path) => path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
}
