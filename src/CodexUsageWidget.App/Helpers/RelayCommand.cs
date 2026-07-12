using CodexUsageWidget.Core.Abstractions;
using System.Windows.Input;

namespace CodexUsageWidget.App.Helpers;

/// <summary>
/// A simple <see cref="ICommand"/> and <see cref="ITrayCommand"/> implementation that delegates to delegates.
/// </summary>
public sealed class RelayCommand : ICommand, ITrayCommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);

        _execute = _ => execute();
        _canExecute = canExecute is null ? null : _ => canExecute();
    }

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    public static void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}
