using System.Globalization;
using System.Text.RegularExpressions;

namespace Aletheia.RAGS.Application.Lexicon;

/// <summary>
/// Parses a proposed fact's value against a lexicon concept's value pattern (<c>date</c>,
/// <c>currency</c>, <c>number</c>, or free text). This is half of the fidelity gate: a value that
/// does not parse is never stored. Dates normalize to <c>yyyy-MM-dd</c>; currencies to a plain
/// decimal string (M/K/million/thousand/billion suffixes honored).
/// </summary>
public static class FactValueParser
{
    public static bool TryParse(string? pattern, string value, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        switch (pattern?.Trim().ToLowerInvariant())
        {
            case "date":
                return TryParseDate(value, out normalized);
            case "currency":
                return TryParseCurrency(value, out normalized);
            case "number":
                return TryParseNumber(value, out normalized);
            default:
                normalized = value.Trim();
                return true;
        }
    }

    private static bool TryParseDate(string value, out string? normalized)
    {
        normalized = null;
        var candidate = value.Trim();

        // Strip a trailing time-of-day clause ("February 24, 2022, at 2:00 p.m. EST") down to the
        // date itself so the parse is not confused by the clock time.
        var dateMatch = Regex.Match(
            candidate,
            @"\b(\d{1,2}[/-]\d{1,2}[/-]\d{2,4}|\d{4}-\d{1,2}-\d{1,2}|[A-Za-z]{3,9}\.?\s+\d{1,2},?\s+\d{4})\b");
        if (dateMatch.Success)
        {
            candidate = dateMatch.Value;
        }

        if (DateTime.TryParse(candidate, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
        {
            normalized = parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return true;
        }

        return false;
    }

    private static bool TryParseCurrency(string value, out string? normalized)
    {
        normalized = null;
        var match = Regex.Match(value, @"\$?\s*([\d,]+(?:\.\d+)?)\s*(M|K|million|thousand|billion)?", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return false;
        }

        var number = decimal.Parse(match.Groups[1].Value.Replace(",", string.Empty), CultureInfo.InvariantCulture);
        var suffix = match.Groups[2].Value.ToLowerInvariant();
        number = suffix switch
        {
            "m" or "million" => number * 1_000_000m,
            "k" or "thousand" => number * 1_000m,
            "b" or "billion" => number * 1_000_000_000m,
            _ => number
        };
        normalized = number.ToString("0.##", CultureInfo.InvariantCulture);
        return true;
    }

    private static bool TryParseNumber(string value, out string? normalized)
    {
        normalized = null;
        var match = Regex.Match(value, @"\d+");
        if (!match.Success)
        {
            return false;
        }

        normalized = match.Value;
        return true;
    }
}
