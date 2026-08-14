using Aletheia.RAGS.Application.Lexicon;

namespace RAGS.UnitTests;

public class FactValueParserTests
{
    [Theory]
    [InlineData("February 24, 2022", "2022-02-24")]
    [InlineData("Feb 24, 2022", "2022-02-24")]
    [InlineData("08/26/2026", "2026-08-26")]
    [InlineData("2026-08-26", "2026-08-26")]
    [InlineData("August 26, 2026, 2:00 PM Pacific Time", "2026-08-26")]
    [InlineData("Proposal Due Date: February 24, 2022, at 2:00 p.m. EST", "2022-02-24")]
    public void TryParse_date_normalizes_common_formats(string value, string expected)
    {
        Assert.True(FactValueParser.TryParse("date", value, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void TryParse_date_rejects_non_dates()
    {
        Assert.False(FactValueParser.TryParse("date", "not a date", out _));
    }

    [Theory]
    [InlineData("$1,200,000", "1200000")]
    [InlineData("$1.2M", "1200000")]
    [InlineData("USD 500K", "500000")]
    [InlineData("2 million", "2000000")]
    public void TryParse_currency_normalizes_amounts(string value, string expected)
    {
        Assert.True(FactValueParser.TryParse("currency", value, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void TryParse_currency_rejects_non_amounts()
    {
        Assert.False(FactValueParser.TryParse("currency", "none", out _));
    }

    [Fact]
    public void TryParse_number_extracts_first_integer()
    {
        Assert.True(FactValueParser.TryParse("number", "25 pages", out var normalized));
        Assert.Equal("25", normalized);
    }

    [Fact]
    public void TryParse_text_accepts_any_non_empty_value()
    {
        Assert.True(FactValueParser.TryParse("text", "Acme Corp", out var normalized));
        Assert.Equal("Acme Corp", normalized);
    }

    [Fact]
    public void TryParse_null_pattern_treats_as_text()
    {
        Assert.True(FactValueParser.TryParse(null, "anything", out var normalized));
        Assert.Equal("anything", normalized);
    }

    [Fact]
    public void TryParse_rejects_empty_value()
    {
        Assert.False(FactValueParser.TryParse("text", "  ", out _));
    }
}
