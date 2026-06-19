using TrackStash.Bootstrap.Output;

namespace TrackStash.Bootstrap.Tests;

public sealed class CommandOutputTests
{
    [Theory]
    [InlineData("json", true)]
    [InlineData("JSON", true)]
    [InlineData("text", false)]
    [InlineData(null, false)]
    public void IsJsonMode_ReturnsExpectedValue(string? format, bool expected)
    {
        Assert.Equal(expected, CommandOutput.IsJsonMode(format ?? string.Empty));
    }
}
