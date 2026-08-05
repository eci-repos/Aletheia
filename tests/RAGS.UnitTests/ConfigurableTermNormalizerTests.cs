using System.Collections.Generic;
using Aletheia.RAGS.Abstractions.Configuration;
using Aletheia.RAGS.Application;
using Aletheia.RAGS.Abstractions.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

public sealed class ConfigurableTermNormalizerTests
{
    private static ITermNormalizer CreateNormalizer(string[]? stopWords = null)
    {
        var options = Options.Create(new TaxonomyOptions { StopWords = stopWords != null ? new List<string>(stopWords) : new List<string>() });
        var loggerMock = new Mock<ILogger<ConfigurableTermNormalizer>>();
        return new ConfigurableTermNormalizer(options, loggerMock.Object);
    }

    [Fact]
    public void Normalize_Removes_StopWords()
    {
        var normalizer = CreateNormalizer(new [] { "the", "and" });
        var result = normalizer.Normalize("The quick brown fox and the dog");
        Assert.Equal("quick brown fox dog", result);
    }

    [Fact]
    public void Normalize_Preserves_Phrase_Exemption()
    {
        var normalizer = CreateNormalizer();
        var result = normalizer.Normalize("3.0 RFP Analysis");
        Assert.Equal("3.0 rfp analysis", result);
    }

    [Fact]
    public void Normalize_Returns_Null_On_Empty()
    {
        var normalizer = CreateNormalizer();
        var result = normalizer.Normalize("   ");
        Assert.Null(result);
    }
}
