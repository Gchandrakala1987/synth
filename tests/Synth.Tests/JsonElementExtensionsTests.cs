using System.Text.Json;
using Synth.Api.Agent;
using FluentAssertions;
using Xunit;

namespace Synth.Tests;

public class JsonElementExtensionsTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void GetString_ReturnsValue_WhenPresent()
    {
        Parse("""{"foo":"bar"}""").GetString("foo").Should().Be("bar");
    }

    [Fact]
    public void GetString_Throws_WhenMissing()
    {
        var act = () => Parse("""{}""").GetString("missing");
        act.Should().Throw<ArgumentException>().WithMessage("*missing*");
    }

    [Fact]
    public void GetStringOrNull_ReturnsNull_WhenAbsent()
    {
        Parse("""{}""").GetStringOrNull("x").Should().BeNull();
    }

    [Fact]
    public void GetIntOr_UsesFallback_WhenAbsent()
    {
        Parse("""{}""").GetIntOr("timeoutMs", 5000).Should().Be(5000);
        Parse("""{"timeoutMs":1234}""").GetIntOr("timeoutMs", 5000).Should().Be(1234);
    }

    [Fact]
    public void GetIntOrNull_ReturnsValueOrNull()
    {
        Parse("""{"x":7}""").GetIntOrNull("x").Should().Be(7);
        Parse("""{}""").GetIntOrNull("x").Should().BeNull();
    }
}
