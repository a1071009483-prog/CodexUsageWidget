namespace CodexUsageWidget.Core.Abstractions;

/// <summary>
/// Framework-agnostic command surface used by the tray context menu. This is deliberately
/// not <see cref="System.Windows.Input.ICommand"/> so that Core remains independent of
/// WPF or Windows Forms.
/// </summary>
public interface ITrayCommand
{
    /// <summary>Whether the command can execute.</summary>
    bool CanExecute(object? parameter);

    /// <summary>Executes the command.</summary>
    void Execute(object? parameter);

    /// <summary>Raised when <see cref="CanExecute"/> may have changed.</summary>
    event EventHandler? CanExecuteChanged;
}
