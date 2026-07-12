using Xunit;

namespace CodexUsageWidget.AcceptanceTests.Testing;

/// <summary>
/// An xUnit <see cref="FactAttribute"/> that skips the test unless every required
/// environment variable is present and non-empty. This keeps acceptance tests
/// out of routine CI runs while still allowing them to execute when a tester
/// explicitly opts in.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class EnvironmentFactAttribute : FactAttribute
{
    /// <summary>
    /// Creates an attribute that requires the specified environment variables.
    /// </summary>
    /// <param name="requiredVariables">The environment variables that must be present.</param>
    public EnvironmentFactAttribute(params string[] requiredVariables)
    {
        RequiredVariables = requiredVariables ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> RequiredVariables { get; }

    /// <inheritdoc/>
    public override string? Skip
    {
        get
        {
            string? parentSkip = base.Skip;
            if (!string.IsNullOrWhiteSpace(parentSkip))
            {
                return parentSkip;
            }

            foreach (string variable in RequiredVariables)
            {
                if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)))
                {
                    return $"Set the {variable} environment variable to run this acceptance test.";
                }
            }

            return null;
        }
        set => base.Skip = value;
    }
}
