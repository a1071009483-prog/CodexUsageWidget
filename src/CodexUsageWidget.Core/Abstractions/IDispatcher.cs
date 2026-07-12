namespace CodexUsageWidget.Core.Abstractions;

/// <summary>
/// Marshals work to the UI thread without binding Core to a specific UI framework.
/// </summary>
public interface IDispatcher
{
    void Invoke(Action action);
}
