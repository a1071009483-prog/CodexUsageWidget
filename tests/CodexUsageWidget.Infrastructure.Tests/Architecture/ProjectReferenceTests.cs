using System.Reflection;
using Xunit;

namespace CodexUsageWidget.Infrastructure.Tests.Architecture;

public sealed class ProjectReferenceTests
{
    [Fact]
    public void InfrastructureProjectReferenceProducesLoadableAssemblies()
    {
        Assembly infrastructure = Assembly.Load("CodexUsageWidget.Infrastructure");
        string coreAssemblyPath = Path.Combine(AppContext.BaseDirectory, "CodexUsageWidget.Core.dll");

        Assert.Equal("CodexUsageWidget.Infrastructure", infrastructure.GetName().Name);
        Assert.True(File.Exists(coreAssemblyPath), $"Core project output was not copied to '{coreAssemblyPath}'.");
    }
}
