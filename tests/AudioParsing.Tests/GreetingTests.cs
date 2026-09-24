using Xunit;

namespace AudioParsing.Tests;

public sealed class GreetingTests
{
    [Fact]
    public void CreateReturnsTheApplicationGreeting()
    {
        string result = Greeting.Create();

        Assert.Equal("AudioParsing is ready.", result);
    }
}
