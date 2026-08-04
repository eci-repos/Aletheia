using Aletheia.RAGS.Abstractions.Configuration;
using Aletheia.RAGS.Application;
using Microsoft.Extensions.Options;

namespace RAGS.UnitTests.FeatureFlags;

public sealed class InternalSearchGateTests
{
    [Fact]
    public void ShowInternalSearch_is_false_by_default()
    {
        var gate = new InternalSearchGate(Options.Create(new FeatureFlagsOptions()));

        Assert.False(gate.ShowInternalSearch);
    }

    [Fact]
    public void ShowInternalSearch_reflects_configured_flag()
    {
        var gate = new InternalSearchGate(Options.Create(new FeatureFlagsOptions { ShowInternalSearch = true }));

        Assert.True(gate.ShowInternalSearch);
    }
}
