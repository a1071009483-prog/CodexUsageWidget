using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Infrastructure.Tests.Windows;

internal sealed class FakeTrayCommandSource : ITrayCommandSource
{
    public string ShowHideHeader { get; set; } = "隐藏";
    public string PauseResumeHeader { get; set; } = "暂停自动触发";
    public bool StartWithWindows { get; set; }

    public ITrayCommand ShowHideCommand { get; set; } = new FakeCommand();
    public ITrayCommand RefreshNowCommand { get; set; } = new FakeCommand();
    public ITrayCommand ToggleAutomationCommand { get; set; } = new FakeCommand();
    public ITrayCommand OpenAuditCommand { get; set; } = new FakeCommand();
    public ITrayCommand ReconnectCommand { get; set; } = new FakeCommand();
    public ITrayCommand ExitCommand { get; set; } = new FakeCommand();
}

internal sealed class FakeCommand : ITrayCommand
{
    public int ExecuteCount { get; private set; }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => ExecuteCount++;

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
