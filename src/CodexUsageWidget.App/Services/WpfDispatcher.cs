using System.Windows;
using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.App.Services;

/// <summary>
/// WPF dispatcher that marshals callbacks to the application UI thread.
/// </summary>
public sealed class WpfDispatcher : IDispatcher
{
    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (System.Windows.Application.Current?.Dispatcher is null)
        {
            action();
            return;
        }

        if (System.Windows.Application.Current.Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            System.Windows.Application.Current.Dispatcher.Invoke(action);
        }
    }
}
