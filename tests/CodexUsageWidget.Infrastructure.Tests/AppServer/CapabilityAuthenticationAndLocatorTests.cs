using CodexUsageWidget.Infrastructure.AppServer;
using CodexUsageWidget.Infrastructure.AppServer.Protocol;
using Xunit;

namespace CodexUsageWidget.Infrastructure.Tests.AppServer;

public sealed class CapabilityAuthenticationAndLocatorTests
{
    [Fact]
    public void CapabilityDiagnosticsReadsSchemaAndReportsMissingMethods()
    {
        const string schema = """
            {"oneOf":[
              {"properties":{"method":{"enum":["initialize"]}}},
              {"properties":{"method":{"enum":["account/read"]}}},
              {"properties":{"method":{"enum":["model/list"]}}}
            ]}
            """;
        var diagnostics = new AppServerCapabilityDiagnostics();

        IReadOnlySet<string> methods = diagnostics.ReadMethodsFromSchema(schema);
        AppServerCapabilityResult result = diagnostics.Evaluate(methods);

        Assert.Contains("initialize", methods);
        Assert.False(result.IsCompatible);
        Assert.Contains("thread/delete", result.MissingMethods);
        Assert.DoesNotContain("initialize", result.MissingMethods);
    }

    [Fact]
    public void CapabilityDiagnosticsAcceptsTheCompleteRequiredSet()
    {
        var diagnostics = new AppServerCapabilityDiagnostics();
        AppServerCapabilityResult result = diagnostics.Evaluate(
            AppServerCapabilityDiagnostics.RequiredMethods);

        Assert.True(result.IsCompatible);
        Assert.Empty(result.MissingMethods);
    }

    [Theory]
    [InlineData(true, null, AuthenticationState.Required)]
    [InlineData(false, "apiKey", AuthenticationState.Unsupported)]
    [InlineData(false, "amazonBedrock", AuthenticationState.Unsupported)]
    [InlineData(false, "chatgpt", AuthenticationState.Supported)]
    public void AuthenticationEvaluatorOnlySupportsChatGptAccounts(
        bool requiresAuth,
        string? accountType,
        AuthenticationState expected)
    {
        var evaluator = new AccountAuthenticationEvaluator();
        AccountDescriptor? account = accountType is null
            ? null
            : new AccountDescriptor(accountType, "alice@example.com", "plus");

        AuthenticationAssessment assessment = evaluator.Evaluate(
            new AccountReadResponse(requiresAuth, account));

        Assert.Equal(expected, assessment.State);
        Assert.Equal("global", assessment.WorkspaceIdentity);
        Assert.DoesNotContain("alice@example.com", assessment.Diagnostic, StringComparison.OrdinalIgnoreCase);
        if (expected == AuthenticationState.Supported)
        {
            Assert.NotNull(assessment.IdentityMaterial);
        }
    }

    [Fact]
    public void ExecutableLocatorUsesConfiguredThenEnvironmentThenPath()
    {
        string? environmentValue = @"C:\env\codex.exe";
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\configured\codex.exe",
            @"C:\env\codex.exe",
            @"C:\path\codex.exe",
        };
        var locator = new CodexExecutableLocator(
            name => name == "CODEX_EXECUTABLE" ? environmentValue : null,
            existing.Contains,
            command => command.StartsWith("codex", StringComparison.OrdinalIgnoreCase)
                ? @"C:\path\codex.exe"
                : null);

        CodexExecutableResolution configured = locator.Locate(@"C:\configured\codex.exe");
        Assert.True(configured.Found);
        Assert.Equal("configured", configured.Source);

        CodexExecutableResolution environment = locator.Locate();
        Assert.Equal("environment", environment.Source);

        environmentValue = null;
        CodexExecutableResolution path = locator.Locate();
        Assert.Equal("path", path.Source);
    }

    [Fact]
    public void ExecutableLocatorReportsUnavailableWithoutGuessing()
    {
        var locator = new CodexExecutableLocator(_ => null, _ => false, _ => null);

        CodexExecutableResolution resolution = locator.Locate();

        Assert.False(resolution.Found);
        Assert.Null(resolution.Command);
        Assert.Contains("not found", resolution.Diagnostic, StringComparison.OrdinalIgnoreCase);
    }
}
