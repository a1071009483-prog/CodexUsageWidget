using System.Collections.Frozen;
using System.Text.Json;
using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Infrastructure.AppServer.Protocol;

namespace CodexUsageWidget.Infrastructure.AppServer;

/// <summary>
/// Factory for a non-generating startup capability preflight that can be injected into
/// <see cref="AppServerSupervisor"/>. The preflight starts a short-lived App Server
/// process, performs the JSON-RPC handshake, and inspects the advertised capabilities.
/// It never invokes <c>thread/start</c> or <c>turn/start</c>.
/// </summary>
public static class AppServerCapabilityPreflight
{
    /// <summary>
    /// Returns a preflight delegate that creates a temporary App Server process using the
    /// supplied host configuration, evaluates the method inventory advertised in the
    /// <c>initialize</c> response, and shuts the temporary process down.
    /// </summary>
    /// <remarks>
    /// The returned delegate follows the <see cref="AppServerSupervisor"/> preflight
    /// contract: it does not throw for ordinary failure conditions; instead it returns an
    /// <see cref="AppServerCapabilityResult"/> with <c>IsCompatible == false</c>.
    /// <see cref="OperationCanceledException"/> is rethrown so the supervisor can stop
    /// cleanly when the caller cancels.
    /// </remarks>
    public static Func<CancellationToken, Task<AppServerCapabilityResult>> ForProcess(
        IProcessHost processHost,
        ProcessStartRequest startRequest,
        ClientInformation clientInformation,
        TimeSpan gracefulStopDelay,
        IDelay delay,
        IRedactingLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(processHost);
        ArgumentNullException.ThrowIfNull(startRequest);
        ArgumentNullException.ThrowIfNull(clientInformation);
        ArgumentNullException.ThrowIfNull(delay);

        return async cancellationToken =>
        {
            var diagnostics = new AppServerCapabilityDiagnostics();
            await using var process = new AppServerProcess(
                processHost,
                startRequest,
                clientInformation,
                gracefulStopDelay,
                delay,
                log);

            try
            {
                _ = await process.StartAsync(cancellationToken).ConfigureAwait(false);

                InitializeResponse? initResult = process.InitializeResult;
                if (initResult?.Capabilities is { ValueKind: JsonValueKind.Object } schema)
                {
                    IReadOnlySet<string> advertised = diagnostics.ReadMethodsFromSchema(
                        schema.GetRawText());
                    return diagnostics.Evaluate(advertised);
                }

                // The App Server did not advertise a capability schema. Fall back to a
                // fail-open result: the process launched and the handshake succeeded, which
                // is the strongest non-generating guarantee available. Required-method
                // diagnostics require a schema from the server.
                return new AppServerCapabilityResult(
                    true,
                    Array.Empty<string>(),
                    new HashSet<string>(StringComparer.Ordinal).ToFrozenSet());
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return new AppServerCapabilityResult(
                    false,
                    AppServerCapabilityDiagnostics.RequiredMethods.ToArray(),
                    new HashSet<string>(StringComparer.Ordinal).ToFrozenSet());
            }
        };
    }
}
