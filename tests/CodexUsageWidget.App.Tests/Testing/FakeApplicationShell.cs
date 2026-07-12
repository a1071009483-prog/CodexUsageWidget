using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.App.Tests.Testing;

internal sealed class FakeApplicationShell : IApplicationShell
{
    public bool ShowMainWindowCalled { get; private set; }
    public bool HideMainWindowCalled { get; private set; }
    public bool OpenAuditWindowCalled { get; private set; }
    public bool ShutdownCalled { get; private set; }

    public void ShowMainWindow() => ShowMainWindowCalled = true;

    public void HideMainWindow() => HideMainWindowCalled = true;

    public void OpenAuditWindow() => OpenAuditWindowCalled = true;

    public void Shutdown() => ShutdownCalled = true;
}
