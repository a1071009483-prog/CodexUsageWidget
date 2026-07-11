using System.Reflection;
using Xunit;

namespace CodexUsageWidget.App.Tests.Architecture;

public sealed class WpfProjectTests
{
    [Fact]
    public void AppProjectBuildsAWindowsPresentationApplication()
    {
        Assembly appAssembly = Assembly.Load("CodexUsageWidget.App");
        Type? applicationType = appAssembly.GetType("CodexUsageWidget.App.App");

        Assert.NotNull(applicationType);
        Assert.Equal("System.Windows.Application", applicationType.BaseType?.FullName);
        Assert.NotNull(applicationType.GetMethod("Main", BindingFlags.Public | BindingFlags.Static));
    }
}
