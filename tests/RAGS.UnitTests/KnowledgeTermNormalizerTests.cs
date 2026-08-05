using Aletheia.RAGS.Abstractions.Models;

namespace RAGS.UnitTests;

public sealed class KnowledgeTermNormalizerTests
{
    [Theory]
    [InlineData("RFP", "RFP")]
    [InlineData("rfp", "RFP")]
    [InlineData("Rfp", "RFP")]
    [InlineData("Rpf", "RFP")]
    [InlineData("  rfp   ", "RFP")]
    [InlineData("CMP", "CMP")]
    [InlineData("llm", "LLM")]
    public void NormalizeLabel_canonicalizes_repository_acronyms(string value, string expected)
    {
        Assert.Equal(expected, KnowledgeTermNormalizer.NormalizeLabel(value));
    }

    [Fact]
    public void GetLookupAliases_includes_legacy_rfp_spellings()
    {
        var aliases = KnowledgeTermNormalizer.GetLookupAliases("RFP");

        Assert.Contains("RFP", aliases);
        Assert.Contains("Rfp", aliases);
        Assert.Contains("Rpf", aliases);
        Assert.Contains("rfp", aliases);
        Assert.Contains("rpf", aliases);
    }
}
