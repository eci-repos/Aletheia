using System.Text.Json;
using Microsoft.SemanticKernel;

namespace Aletheia.RAGS.Application.GraphIntelligence;

/// <summary>
/// Extracts token counts from SemanticKernel <see cref="ChatMessageContent.Metadata"/>.
/// SK 1.78 does not expose a clean <c>Usage</c> property on <see cref="ChatMessageContent"/>;
/// usage rides in the message metadata with provider-specific keys and shapes. This helper
/// sums the known key/property names across OpenAI, Azure OpenAI, and Ollama backends so the
/// token budget can be enforced from real usage when the provider reports it.
/// </summary>
public static class TokenUsageHelper
{
    private static readonly string[] InputKeys =
    {
        "InputTokenCount", "InputTokens", "PromptTokenCount", "PromptTokens",
        "prompt_tokens", "prompt_eval_count", "input_tokens", "inputTokens",
    };

    private static readonly string[] OutputKeys =
    {
        "OutputTokenCount", "OutputTokens", "CompletionTokenCount", "CompletionTokens",
        "completion_tokens", "eval_count", "output_tokens", "outputTokens",
    };

    private static readonly string[] TotalKeys =
    {
        "TotalTokenCount", "TotalTokens", "total_tokens",
    };

    /// <summary>
    /// Returns the total (input + output) token count reported by the provider,
    /// or 0 when the response carries no usage information.
    /// </summary>
    public static int GetTotalTokens(ChatMessageContent? content)
    {
        if (content?.Metadata is not { } metadata)
        {
            return 0;
        }

        var input = ExtractTokens(metadata, InputKeys);
        var output = ExtractTokens(metadata, OutputKeys);
        if (input > 0 || output > 0)
        {
            return input + output;
        }

        // Some connectors nest usage under a "Usage" entry (e.g. OpenAI's ChatTokenUsage).
        if (metadata.TryGetValue("Usage", out var usage) && usage is not null)
        {
            var usageInput = ExtractTokens(usage, InputKeys);
            var usageOutput = ExtractTokens(usage, OutputKeys);
            if (usageInput > 0 || usageOutput > 0)
            {
                return usageInput + usageOutput;
            }
        }

        // Fall back to a total-only figure when input/output breakdowns are absent.
        return ExtractTokens(metadata, TotalKeys);
    }

    private static int ExtractTokens(object? source, IReadOnlyList<string> keys)
    {
        if (source is null)
        {
            return 0;
        }

        if (source is IDictionary<string, object?> dictionary)
        {
            foreach (var (key, value) in dictionary)
            {
                if (value is not null && Contains(keys, key))
                {
                    var parsed = ToInt(value);
                    if (parsed > 0)
                    {
                        return parsed;
                    }
                }
            }

            return 0;
        }

        // Provider-specific usage objects (e.g. OpenAI ChatTokenUsage) expose the counts
        // as public properties; read them by name so we need no reference to their SDK.
        var type = source.GetType();
        foreach (var key in keys)
        {
            var property = type.GetProperty(key);
            if (property is not null && property.GetValue(source) is { } value)
            {
                var parsed = ToInt(value);
                if (parsed > 0)
                {
                    return parsed;
                }
            }
        }

        return 0;
    }

    private static bool Contains(IReadOnlyList<string> keys, string key)
    {
        for (var i = 0; i < keys.Count; i++)
        {
            if (string.Equals(keys[i], key, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static int ToInt(object value)
    {
        return value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            float f => (int)f,
            JsonElement element => ToInt(element),
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => 0,
        };
    }

    private static int ToInt(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var number))
        {
            return (int)number;
        }

        if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out var parsed))
        {
            return parsed;
        }

        return 0;
    }
}
