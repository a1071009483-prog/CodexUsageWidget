using System.Text;
using System.Threading.Channels;
using Xunit;

namespace CodexUsageWidget.Infrastructure.Tests.AppServer;

internal sealed class AsyncLineTransport
{
    public ChannelLineReader ServerOutput { get; } = new();

    public ChannelLineWriter ClientInput { get; } = new();
}

internal sealed class ChannelLineReader : TextReader
{
    private readonly Channel<string> _lines = Channel.CreateUnbounded<string>();

    public void WriteLine(string line)
    {
        // Best-effort write. If the reader has already been closed by a pump fault,
        // the line is simply dropped, which matches the behavior of a disconnected
        // process stdout and avoids races in tests that intentionally write late frames.
        _lines.Writer.TryWrite(line);
    }

    public void Complete(Exception? error = null) => _lines.Writer.TryComplete(error);

    public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _lines.Reader.ReadAsync(cancellationToken);
        }
        catch (ChannelClosedException exception) when (exception.InnerException is null)
        {
            return null;
        }
    }
}

internal sealed class ChannelLineWriter : TextWriter
{
    private readonly Channel<string> _lines = Channel.CreateUnbounded<string>();

    public override Encoding Encoding => Encoding.UTF8;

    public override Task WriteLineAsync(string? value)
    {
        if (!_lines.Writer.TryWrite(value ?? string.Empty))
        {
            throw new InvalidOperationException("The client-input channel is closed.");
        }

        return Task.CompletedTask;
    }

    public override Task WriteLineAsync(
        ReadOnlyMemory<char> buffer,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return WriteLineAsync(buffer.ToString());
    }

    public async Task<string> ReadLineAsync(CancellationToken cancellationToken = default) =>
        await _lines.Reader.ReadAsync(cancellationToken);
}
