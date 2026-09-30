using NMSE.Core;
using Xunit;

namespace NMSE.Tests;

/// <summary>
/// Tests for <see cref="ExternalChangeDecision"/>: Prompt asks for every change, AutoReload
/// applies changes silently and Ignore never reacts; deletions always prompt.
/// </summary>
public class ExternalChangeDecisionTests
{
    [Theory]
    [InlineData(false, false, "Prompt", "None")]
    [InlineData(false, false, "AutoReload", "None")]
    [InlineData(false, false, "Ignore", "None")]
    [InlineData(true, false, "Prompt", "Prompt")]
    [InlineData(true, false, "AutoReload", "Reload")]
    [InlineData(true, false, "Ignore", "None")]
    [InlineData(false, true, "Prompt", "Prompt")]
    [InlineData(false, true, "AutoReload", "Prompt")]
    [InlineData(false, true, "Ignore", "None")]
    [InlineData(true, true, "Prompt", "Prompt")]
    [InlineData(true, true, "AutoReload", "Prompt")]
    [InlineData(true, true, "Ignore", "None")]
    public void Decide_ReturnsExpectedAction(bool changed, bool deleted, string mode, string expected)
    {
        var parsed = Enum.Parse<ExternalChangeDecision.Mode>(mode);
        Assert.Equal(expected, ExternalChangeDecision.Decide(changed, deleted, parsed).ToString());
    }
}
