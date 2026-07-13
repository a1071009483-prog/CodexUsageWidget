using System.Diagnostics.CodeAnalysis;
using CodexUsageWidget.Infrastructure.AppServer.Protocol;

namespace CodexUsageWidget.Infrastructure.AppServer;

public enum AuthenticationState
{
    Supported,
    Required,
    Unsupported,
}

public sealed record AuthenticationAssessment(
    AuthenticationState State,
    string? PlanType,
    string? IdentityMaterial,
    string WorkspaceIdentity,
    string Diagnostic);

public sealed class AccountAuthenticationEvaluator
{
    private const string GlobalWorkspaceIdentity = "global";

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "This boundary is intentionally exposed as an injectable instance service.")]
    public AuthenticationAssessment Evaluate(AccountReadResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.Account is null)
        {
            return new AuthenticationAssessment(
                AuthenticationState.Required,
                null,
                null,
                GlobalWorkspaceIdentity,
                "ChatGPT authentication is required.");
        }

        AccountDescriptor account = response.Account;
        if (!string.Equals(account.Type, "chatgpt", StringComparison.OrdinalIgnoreCase))
        {
            return new AuthenticationAssessment(
                AuthenticationState.Unsupported,
                account.PlanType,
                null,
                GlobalWorkspaceIdentity,
                "The current authentication type is not supported.");
        }

        return new AuthenticationAssessment(
            AuthenticationState.Supported,
            account.PlanType,
            account.Email,
            GlobalWorkspaceIdentity,
            "ChatGPT authentication is supported.");
    }
}
