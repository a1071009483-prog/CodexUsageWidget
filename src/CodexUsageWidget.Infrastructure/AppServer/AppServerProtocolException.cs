using System.Text.Json;

namespace CodexUsageWidget.Infrastructure.AppServer;

public enum AppServerProtocolErrorKind
{
    RemoteError,
    MethodNotFound,
    MalformedMessage,
    Disconnected,
}
public sealed class AppServerProtocolException : Exception
{
    public AppServerProtocolException(
        AppServerProtocolErrorKind kind,
        string message,
        long? code = null,
        JsonElement? data = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        Code = code;
        DataElement = data;
    }

    public AppServerProtocolErrorKind Kind { get; }

    public long? Code { get; }

    public JsonElement? DataElement { get; }
}
