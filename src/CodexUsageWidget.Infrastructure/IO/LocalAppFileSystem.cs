using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Infrastructure.IO;

/// <summary>
/// Production implementation of <see cref="IAppFileSystem"/> using local file I/O.
/// </summary>
public sealed class LocalAppFileSystem : IAppFileSystem
{
    public async Task<AppFileReadResult> ReadTextAsync(
        AppFileReadRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            string content = await File.ReadAllTextAsync(request.Path, cancellationToken)
                .ConfigureAwait(false);
            return new AppFileReadResult(true, content);
        }
        catch (FileNotFoundException)
        {
            return new AppFileReadResult(false, null, "not-found");
        }
        catch (DirectoryNotFoundException)
        {
            return new AppFileReadResult(false, null, "not-found");
        }
        catch (Exception exception)
        {
            return new AppFileReadResult(false, null, exception.GetType().Name);
        }
    }

    public async Task<AppFileWriteResult> WriteTextAsync(
        AppFileWriteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            string? directory = Path.GetDirectoryName(request.Path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(request.Path, request.Content, cancellationToken)
                .ConfigureAwait(false);
            return new AppFileWriteResult(true);
        }
        catch (Exception exception)
        {
            return new AppFileWriteResult(false, exception.GetType().Name);
        }
    }
}
