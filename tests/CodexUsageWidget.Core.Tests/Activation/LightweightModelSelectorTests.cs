using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Core.Activation;
using Xunit;

namespace CodexUsageWidget.Core.Tests.Activation;

public sealed class LightweightModelSelectorTests
{
    private static ModelCandidate Candidate(
        string id,
        string model,
        bool isDefault = false,
        params string[] efforts) =>
        new(id, model, model, isDefault, efforts.Length > 0 ? efforts : ["minimal"]);

    [Fact]
    public void SelectsNewestMemberOfHighestPriorityRecognizedFamily()
    {
        var catalog = new[]
        {
            Candidate("openai/gpt-4o-mini-2024-07-18", "gpt-4o-mini"),
            Candidate("openai/gpt-4o-mini-2024-09-12", "gpt-4o-mini"),
            Candidate("default-model", "default", isDefault: true),
        };

        ModelSelectionResult? result = LightweightModelSelector.Select(catalog);

        Assert.NotNull(result);
        Assert.False(result.UsedFallback);
        Assert.Equal("openai/gpt-4o-mini-2024-09-12", result.Selected.Id);
        Assert.Equal("gpt-4o-mini", result.Selected.Model);
    }

    [Fact]
    public void RespectsFamilyPriorityOrder()
    {
        var catalog = new[]
        {
            Candidate("openai/o3-mini", "o3-mini"),
            Candidate("openai/o4-mini", "o4-mini"),
            Candidate("openai/gpt-4o-mini", "gpt-4o-mini"),
        };

        ModelSelectionResult? result = LightweightModelSelector.Select(catalog);

        Assert.NotNull(result);
        Assert.Equal("gpt-4o-mini", result.Selected.Model);
    }

    [Fact]
    public void FallsBackToDefaultWhenNoRecognizedFamilyExists()
    {
        var catalog = new[]
        {
            Candidate("openai/gpt-4o", "gpt-4o"),
            Candidate("openai/some-default", "some-default", isDefault: true),
        };

        ModelSelectionResult? result = LightweightModelSelector.Select(catalog);

        Assert.NotNull(result);
        Assert.True(result.UsedFallback);
        Assert.Equal("some-default", result.Selected.Model);
    }

    [Fact]
    public void ReturnsNullWhenNoRecognizedFamilyAndNoDefault()
    {
        var catalog = new[]
        {
            Candidate("openai/gpt-4o", "gpt-4o"),
            Candidate("openai/claude-sonnet", "claude-sonnet"),
        };

        ModelSelectionResult? result = LightweightModelSelector.Select(catalog);

        Assert.Null(result);
    }

    [Fact]
    public void ReturnsNullWhenMultipleDefaultsExist()
    {
        var catalog = new[]
        {
            Candidate("a", "a", isDefault: true),
            Candidate("b", "b", isDefault: true),
        };

        ModelSelectionResult? result = LightweightModelSelector.Select(catalog);

        Assert.Null(result);
    }

    [Fact]
    public void OrdersByEmbeddedVersionAcrossDifferentFormats()
    {
        var catalog = new[]
        {
            Candidate("azure/gpt-4o-mini-1.0", "gpt-4o-mini"),
            Candidate("azure/gpt-4o-mini-1.5", "gpt-4o-mini"),
            Candidate("azure/gpt-4o-mini-1.10", "gpt-4o-mini"),
        };

        ModelSelectionResult? result = LightweightModelSelector.Select(catalog);

        Assert.NotNull(result);
        Assert.Equal("azure/gpt-4o-mini-1.10", result.Selected.Id);
    }

    [Fact]
    public void EmptyCatalogReturnsNull()
    {
        ModelSelectionResult? result = LightweightModelSelector.Select(Array.Empty<ModelCandidate>());

        Assert.Null(result);
    }

    [Fact]
    public void RefreshedCatalogCanChangeSelection()
    {
        ModelSelectionResult? first = LightweightModelSelector.Select(new[]
        {
            Candidate("v1", "gpt-4o-mini"),
        });

        ModelSelectionResult? second = LightweightModelSelector.Select(new[]
        {
            Candidate("v1", "gpt-4o-mini"),
            Candidate("v2", "gpt-4o-mini"),
        });

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal("v1", first.Selected.Id);
        Assert.Equal("v2", second.Selected.Id);
    }
}
