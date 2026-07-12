using CodexUsageWidget.Infrastructure.Windows;
using Xunit;

namespace CodexUsageWidget.Infrastructure.Tests.Windows;

public sealed class TrayIconServiceTests
{
    [Fact]
    public void InitializeCreatesMenuWithRequiredItems()
    {
        FakeNotifyIcon icon = new();
        FakeTrayCommandSource source = new();
        TrayIconService tray = new(icon, "Test");

        tray.Initialize(source);

        Assert.NotNull(icon.ContextMenuStrip);
        string[] items = icon.ContextMenuStrip.Items
            .Cast<System.Windows.Forms.ToolStripItem>()
            .Select(i => i.Text)
            .Where(t => !string.IsNullOrEmpty(t))
            .OfType<string>()
            .ToArray();

        Assert.Contains("隐藏", items);
        Assert.Contains("立即刷新", items);
        Assert.Contains("暂停自动触发", items);
        Assert.Contains("开机启动", items);
        Assert.Contains("审计日志", items);
        Assert.Contains("重新连接", items);
        Assert.Contains("退出", items);
        Assert.DoesNotContain("强制消耗", items);
    }

    [Fact]
    public void DoubleClickExecutesShowHideCommand()
    {
        FakeNotifyIcon icon = new();
        FakeTrayCommandSource source = new();
        TrayIconService tray = new(icon, "Test");
        tray.Initialize(source);

        icon.RaiseDoubleClick();

        Assert.Equal(1, ((FakeCommand)source.ShowHideCommand).ExecuteCount);
    }

    [Fact]
    public void SetPauseResumeLabelUpdatesMenuText()
    {
        FakeNotifyIcon icon = new();
        FakeTrayCommandSource source = new();
        TrayIconService tray = new(icon, "Test");
        tray.Initialize(source);

        tray.SetPauseResumeLabel("恢复自动触发");

        var pauseItem = icon.ContextMenuStrip!.Items
            .Cast<System.Windows.Forms.ToolStripItem>()
            .OfType<System.Windows.Forms.ToolStripMenuItem>()
            .First(i => i.Text == "暂停自动触发" || i.Text == "恢复自动触发");
        Assert.Equal("恢复自动触发", pauseItem.Text);
    }

    [Fact]
    public void SetStartWithWindowsCheckedUpdatesMenuCheck()
    {
        FakeNotifyIcon icon = new();
        FakeTrayCommandSource source = new();
        TrayIconService tray = new(icon, "Test");
        tray.Initialize(source);

        tray.SetStartWithWindowsChecked(true);

        var startItem = icon.ContextMenuStrip!.Items
            .Cast<System.Windows.Forms.ToolStripItem>()
            .OfType<System.Windows.Forms.ToolStripMenuItem>()
            .First(i => i.Text == "开机启动");
        Assert.True(startItem.Checked);
    }
}
