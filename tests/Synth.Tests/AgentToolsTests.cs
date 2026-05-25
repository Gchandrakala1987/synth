using System.Text.Json;
using Synth.Api.Agent;
using FluentAssertions;
using Xunit;

namespace Synth.Tests;

public class AgentToolsTests
{
    [Fact]
    public void All_ContainsCoreBrowserTools()
    {
        var names = AgentTools.All.Select(t => t.FunctionName).ToHashSet();

        names.Should().Contain(new[]
        {
            AgentTools.Navigate, AgentTools.Click, AgentTools.Fill,
            AgentTools.ReadPage, AgentTools.Assert, AgentTools.Finish
        });
    }

    [Theory]
    [InlineData("navigate")]
    [InlineData("click")]
    [InlineData("fill")]
    [InlineData("read_page")]
    [InlineData("assert")]
    [InlineData("finish_run")]
    public void EveryTool_HasValidJsonSchema(string toolName)
    {
        var tool = AgentTools.All.Single(t => t.FunctionName == toolName);

        var schemaJson = tool.FunctionParameters.ToString();
        var parsed = JsonDocument.Parse(schemaJson).RootElement;

        parsed.GetProperty("type").GetString().Should().Be("object");
        parsed.TryGetProperty("properties", out _).Should().BeTrue();
        parsed.TryGetProperty("required", out _).Should().BeTrue();
    }

    [Fact]
    public void FinishTool_RequiresSummaryAndFindings()
    {
        var finish = AgentTools.All.Single(t => t.FunctionName == AgentTools.Finish);

        var schema = JsonDocument.Parse(finish.FunctionParameters.ToString()).RootElement;
        var required = schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToArray();

        required.Should().Contain("summary");
        required.Should().Contain("overallSeverity");
        required.Should().Contain("findings");
    }
}
