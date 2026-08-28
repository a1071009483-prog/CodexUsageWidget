using CodexUsageWidget.App.Services;
using Xunit;

namespace CodexUsageWidget.App.Tests.Services;

public sealed class ApplicationVersionTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.2")]
    [InlineData("1.0.0+abcdef", "1.0.0")]
    public void NormalizeRemovesBuildMetadata(string input, string expected)
    {
        Assert.Equal(expected, ApplicationVersion.Normalize(input));
    }

    [Fact]
    public void CurrentIsNotEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(ApplicationVersion.Current));
    }
}
