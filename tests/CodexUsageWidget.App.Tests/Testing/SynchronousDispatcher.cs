using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.App.Tests.Testing;

internal sealed class SynchronousDispatcher : IDispatcher
{
    public void Invoke(Action action) => action();
}
