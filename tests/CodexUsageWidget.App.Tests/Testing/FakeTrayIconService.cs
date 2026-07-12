using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.App.Tests.Testing;

internal sealed class FakeTrayIconService : ITrayIconService
{
    public bool IsInitialized { get; private set; }
    public bool IsShown { get; private set; }
    public string? PauseResumeLabel { get; private set; }
    public string? ShowHideLabel { get; private set; }
    public bool? StartWithWindowsChecked { get; private set; }

    public void Initialize(ITrayCommandSource commandSource) => IsInitialized = true;

    public void Show() => IsShown = true;

    public void Hide() => IsShown = false;

    public void SetPauseResumeLabel(string label) => PauseResumeLabel = label;

    public void SetShowHideLabel(string label) => ShowHideLabel = label;

    public void SetStartWithWindowsChecked(bool isChecked) => StartWithWindowsChecked = isChecked;

    public void Dispose()
    {
    }
}
