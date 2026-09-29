using NMSE.Core;
using Xunit;

namespace NMSE.Tests;

/// <summary>
/// Tests for <see cref="ExternalChangeDecision"/>: reload silently when there are no
/// unsaved changes, prompt when there are, and always prompt for deletions.
/// </summary>
public class ExternalChangeDecisionTests
{
    [Theory]
    [InlineData(false, false, false, "None")]
    [InlineData(false, false, true, "None")]
    [InlineData(true, false, false, "Reload")]
    [InlineData(true, false, true, "Prompt")]
    [InlineData(false, true, false, "Prompt")]
    [InlineData(false, true, true, "Prompt")]
    [InlineData(true, true, false, "Prompt")]
    [InlineData(true, true, true, "Prompt")]
    public void Decide_ReturnsExpectedAction(bool changed, bool deleted, bool dirty, string expected)
    {
        Assert.Equal(expected, ExternalChangeDecision.Decide(changed, deleted, dirty).ToString());
    }
}
