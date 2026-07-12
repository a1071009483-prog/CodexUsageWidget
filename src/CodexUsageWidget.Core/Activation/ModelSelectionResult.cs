using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Core.Activation;

/// <summary>
/// The result of selecting a model for activation.
/// </summary>
/// <param name="Selected">The selected model candidate.</param>
/// <param name="UsedFallback">Whether the selection fell back to the server's default candidate.</param>
public sealed record ModelSelectionResult(ModelCandidate Selected, bool UsedFallback);
