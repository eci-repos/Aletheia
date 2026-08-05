using System.Globalization;
using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace Aletheia.RAGS.Application.Planning;

/// <summary>
/// Invokes repository tools through the registered Semantic Kernel.
/// Parses tool names in the form "PluginName.FunctionName" and normalizes
/// common aliases such as "AletheiaKnowledgePlugin.SearchRags" and
/// "RepositoryTool.SearchRepositoryDocuments" to the actual kernel functions.
/// </summary>
public sealed class KernelChatToolInvoker : IChatToolInvoker
{
    private readonly Kernel _kernel;
    private readonly ILogger<KernelChatToolInvoker> _logger;

    public KernelChatToolInvoker(Kernel kernel, ILogger<KernelChatToolInvoker>? logger = null)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<KernelChatToolInvoker>.Instance;
    }

    public async Task<ToolInvocationResponse> InvokeAsync(
        string toolName,
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return new ToolInvocationResponse("Tool name is required.");
        }

        var normalized = NormalizeToolName(toolName);
        if (!TryParseToolName(normalized, out var pluginName, out var functionName))
        {
            return new ToolInvocationResponse($"Tool name '{toolName}' is not in the expected 'PluginName.FunctionName' format.");
        }

        _logger.LogInformation(
            "Invoking chat tool {PluginName}.{FunctionName} (original: {OriginalToolName}).",
            pluginName,
            functionName,
            toolName);

        var function = _kernel.Plugins.GetFunction(pluginName, functionName);
        if (function is null)
        {
            var available = string.Join(", ", _kernel.Plugins.GetFunctionsMetadata().Select(m => $"{m.PluginName}.{m.Name}"));
            _logger.LogWarning(
                "Chat tool {PluginName}.{FunctionName} was not found in the kernel. Available functions: {AvailableFunctions}.",
                pluginName,
                functionName,
                available);
            return new ToolInvocationResponse($"Tool '{pluginName}.{functionName}' is not registered in the kernel.");
        }

        try
        {
            var kernelArguments = new KernelArguments();
            foreach (var argument in arguments)
            {
                kernelArguments[argument.Key] = argument.Value;
            }

            var result = await _kernel.InvokeAsync(function, kernelArguments, cancellationToken).ConfigureAwait(false);
            return NormalizeResult(result.GetValue<object>(), pluginName, functionName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Chat tool {PluginName}.{FunctionName} was cancelled by step timeout.", pluginName, functionName);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chat tool {PluginName}.{FunctionName} failed.", pluginName, functionName);
            return new ToolInvocationResponse($"{pluginName}.{functionName} failed: {ex.Message}");
        }
    }

    private static ToolInvocationResponse NormalizeResult(object? value, string pluginName, string functionName)
    {
        if (value is null)
        {
            return new ToolInvocationResponse($"{pluginName}.{functionName} returned no result.");
        }

        if (value is IReadOnlyList<SearchResult> searchResults)
        {
            return new ToolInvocationResponse(searchResults);
        }

        if (value is IEnumerable<SearchResult> enumerableResults)
        {
            return new ToolInvocationResponse(enumerableResults.ToList());
        }

        if (value is SearchResult singleResult)
        {
            return new ToolInvocationResponse(new[] { singleResult });
        }

        if (value is GlobalSearchResult globalSearchResult)
        {
            var citations = globalSearchResult.Citations?.ToList() ?? new List<string>();
            var sourceId = citations.FirstOrDefault() is { } citation && Guid.TryParse(citation, out var parsed)
                ? parsed
                : Guid.NewGuid();
            var chunk = new Chunk(Guid.NewGuid(), sourceId, globalSearchResult.Answer ?? string.Empty, 0);
            var retrievalStrategy = functionName switch
            {
                "SearchGraphRag" => "graphrag-global",
                "SearchLazyGraphRag" => "lazygraphrag-global",
                "SearchGlobalGraph" => "global-graph",
                _ => "global"
            };
            return new ToolInvocationResponse(new[] { new SearchResult(chunk, 1.0f, citations, retrievalStrategy: retrievalStrategy) });
        }

        if (value is string text)
        {
            var chunk = new Chunk(Guid.NewGuid(), Guid.NewGuid(), text, 0);
            return new ToolInvocationResponse(new[] { new SearchResult(chunk, 1.0f, Array.Empty<string>()) });
        }

        return new ToolInvocationResponse($"{pluginName}.{functionName} returned unsupported type '{value.GetType().Name}'.");
    }

    private static string NormalizeToolName(string toolName)
    {
        // Strip Async suffix if present so callers can use either style.
        if (toolName.EndsWith("Async", StringComparison.OrdinalIgnoreCase))
        {
            toolName = toolName[..^5];
        }

        // Normalize known aliases.
        if (string.Equals(toolName, "RepositoryTool.SearchRepositoryDocuments", StringComparison.OrdinalIgnoreCase))
        {
            return "AletheiaKnowledgePlugin.SearchRags";
        }

        if (string.Equals(toolName, "RepositoryTool.SearchRepositoryGraphRag", StringComparison.OrdinalIgnoreCase))
        {
            return "AletheiaKnowledgePlugin.SearchGraphRag";
        }

        if (string.Equals(toolName, "RepositoryTool.ResolveRepositorySource", StringComparison.OrdinalIgnoreCase))
        {
            return "AletheiaKnowledgePlugin.ResolveKnowledgeSource";
        }

        return toolName;
    }

    private static bool TryParseToolName(string toolName, out string pluginName, out string functionName)
    {
        pluginName = string.Empty;
        functionName = string.Empty;

        var separator = toolName.LastIndexOf('.');
        if (separator <= 0 || separator >= toolName.Length - 1)
        {
            return false;
        }

        pluginName = toolName[..separator];
        functionName = toolName[(separator + 1)..];
        return !string.IsNullOrWhiteSpace(pluginName) && !string.IsNullOrWhiteSpace(functionName);
    }
}
