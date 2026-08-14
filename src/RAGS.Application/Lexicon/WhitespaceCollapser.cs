using System.Text;

namespace Aletheia.RAGS.Application.Lexicon;

/// <summary>
/// Collapses every whitespace run in a string to a single space and records a position map from
/// the collapsed index back to the original index. Used by <c>FactVerifier</c> so a proposer's
/// quoted span matches the extracted text even when line breaks or spacing differ.
/// </summary>
public static class WhitespaceCollapser
{
    public static (string Text, int[] PositionMap) Collapse(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (string.Empty, Array.Empty<int>());
        }

        var builder = new StringBuilder(text.Length);
        var map = new List<int>(text.Length);
        var inWhitespace = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                if (!inWhitespace)
                {
                    builder.Append(' ');
                    map.Add(i);
                    inWhitespace = true;
                }
            }
            else
            {
                builder.Append(c);
                map.Add(i);
                inWhitespace = false;
            }
        }

        return (builder.ToString(), map.ToArray());
    }
}
