using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Infrastructure.AppServer;
using CodexUsageWidget.Infrastructure.AppServer.Protocol;
using Xunit;

namespace CodexUsageWidget.Infrastructure.Tests.AppServer;

public sealed class AppServerAccountIdentityProviderTests
{
    private static readonly AccountAuthenticationEvaluator Evaluator = new();

    [Fact]
    public async Task SupportedAccountReturnsIdentity()
    {
        AppServerAccountIdentityProvider provider = new(
            _ => Task.FromResult(
                new AccountReadResponse(
                    false,
                    new AccountDescriptor("chatgpt", "user@example.com", "plus"))),
            Evaluator);

        AccountIdentity identity = await provider.GetIdentityAsync();

        Assert.Equal("user@example.com", identity.Email);
        Assert.Equal("plus", identity.Plan);
        Assert.Equal("global", identity.WorkspaceScope);
    }

    [Fact]
    public async Task MissingAuthenticationThrows()
    {
        AppServerAccountIdentityProvider provider = new(
            _ => Task.FromResult(new AccountReadResponse(true, null)),
            Evaluator);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetIdentityAsync());

        Assert.Contains("Required", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnsupportedAccountTypeThrows()
    {
        AppServerAccountIdentityProvider provider = new(
            _ => Task.FromResult(
                new AccountReadResponse(
                    false,
                    new AccountDescriptor("github", "user@example.com", "free"))),
            Evaluator);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetIdentityAsync());

        Assert.Contains("Unsupported", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
