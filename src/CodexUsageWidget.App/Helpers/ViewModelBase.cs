using System.ComponentModel;
using System.Runtime.CompilerServices;
using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.App.Helpers;

/// <summary>
/// Base class for view models that raises property-change notifications on the supplied dispatcher.
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    private readonly IDispatcher _dispatcher;

    protected ViewModelBase(IDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    protected IDispatcher Dispatcher => _dispatcher;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(
        ref T storage,
        T value,
        [CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChangedEventHandler? handler = PropertyChanged;
        if (handler is null)
        {
            return;
        }

        _dispatcher.Invoke(() => handler(this, new PropertyChangedEventArgs(propertyName)));
    }
}
