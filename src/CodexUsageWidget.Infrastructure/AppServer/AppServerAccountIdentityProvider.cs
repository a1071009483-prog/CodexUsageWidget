using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Infrastructure.AppServer.Protocol;

namespace CodexUsageWidget.Infrastructure.AppServer;

/// <summary>
/// Resolves the current ChatGPT account identity from a live App Server session.
/// Throws when the session is unavailable or authentication is required/unsupported.
/// </summary>
public sealed class AppServerAccountIdentityProvider : IAccountIdentityProvider
{
    private readonly Func<CancellationToken, Task<AccountReadResponse>> _readAccount;
    private readonly AccountAuthenticationEvaluator _evaluator;

    /// <summary>
    /// Creates a provider that reads the account through the supplied App Server session.
    /// </summary>
    public AppServerAccountIdentityProvider(
        AppServerSupervisor supervisor,
        AccountAuthenticationEvaluator evaluator)
        : this(
            async ct =>
            {
                AppServerGenerationSession? generation = supervisor.CurrentGeneration
                    ?? throw new InvalidOperationException("The App Server session is not available.");
                return await generation.Session.Gateway.ReadAccountAsync(refreshToken: false, ct)
                    .ConfigureAwait(false);
            },
            evaluator)
    {
    }

    /// <summary>
    /// Creates a provider from an explicit account-read delegate. Used for testing.
    /// </summary>
    internal AppServerAccountIdentityProvider(
        Func<CancellationToken, Task<AccountReadResponse>> readAccount,
        AccountAuthenticationEvaluator evaluator)
    {
        _readAccount = readAccount ?? throw new ArgumentNullException(nameof(readAccount));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
    }

    /// <inheritdoc/>
    public async Task<AccountIdentity> GetIdentityAsync(CancellationToken cancellationToken = default)
    {
        AccountReadResponse response = await _readAccount(cancellationToken).ConfigureAwait(false);
        AuthenticationAssessment assessment = _evaluator.Evaluate(response);

        return assessment.State switch
        {
            AuthenticationState.Supported => new AccountIdentity(
                assessment.IdentityMaterial ?? string.Empty,
                assessment.PlanType,
                assessment.WorkspaceIdentity),

            AuthenticationState.Required => throw new InvalidOperationException(
                $"Unsupported authentication state: Required. {assessment.Diagnostic}"),

            AuthenticationState.Unsupported => throw new InvalidOperationException(
                $"Unsupported authentication state: {assessment.State}. {assessment.Diagnostic}"),

            _ => throw new InvalidOperationException(
                $"Unsupported authentication state: {assessment.State}. {assessment.Diagnostic}"),
        };
    }
}
