using System.Text.RegularExpressions;
using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Configuration;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;
using Microsoft.Extensions.Options;

namespace Aletheia.RAGS.Application.SemanticKernel;

public sealed class MetadataKnowledgeSourceResolver : IKnowledgeSourceResolver
{
    private const int CandidateLimit = 200;

    private static readonly Regex TokenPattern = new(@"\b[A-Za-z0-9][A-Za-z0-9\-]{1,}\b", RegexOptions.Compiled);

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "about", "activity", "activities", "are", "can", "defined", "document", "documents", "file", "files",
        "for", "from", "have", "in", "into", "is", "kb", "knowledge", "last", "latest", "most", "of", "on",
        "recent", "related", "requirements", "requirement", "the", "to", "what", "which", "with", "within"
    };

    private readonly IMetadataRepository _metadataRepository;
    private readonly CopilotOptions _options;

    public MetadataKnowledgeSourceResolver(IMetadataRepository metadataRepository, IOptions<CopilotOptions>? options = null)
    {
        _metadataRepository = metadataRepository ?? throw new ArgumentNullException(nameof(metadataRepository));
        _options = options?.Value ?? new CopilotOptions();
    }

    public async Task<Result<KnowledgeSource?>> ResolveAsync(string userMessage, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return Result<KnowledgeSource?>.Success(null);
        }

        var tokens = ExpandAliases(ExtractTokens(userMessage));
        if (tokens.Count == 0)
        {
            return Result<KnowledgeSource?>.Success(null);
        }

        var result = await _metadataRepository
            .SearchAsync(new SearchRequest(null, 1, CandidateLimit), cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure || result.Value is null)
        {
            return Result<KnowledgeSource?>.Failure(result.Error ?? "Knowledge source metadata search failed.");
        }

        var ranked = result.Value.Items
            .Select(metadata => new
            {
                Metadata = metadata,
                Score = Score(metadata.Descriptor.FileName, tokens)
            })
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Metadata.UploadedAt)
            .ToList();

        if (ranked.Count == 0)
        {
            return Result<KnowledgeSource?>.Success(null);
        }

        var selected = ranked[0].Metadata;
        return Result<KnowledgeSource?>.Success(new KnowledgeSource(
            selected.Descriptor.FileId,
            selected.Descriptor.FileName,
            selected.UploadedAt));
    }

    private static IReadOnlyList<string> ExtractTokens(string value)
    {
        return TokenPattern.Matches(value)
            .Select(match => match.Value.Trim('-'))
            .Where(token => token.Length > 1 && !StopWords.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<string> ExpandAliases(IReadOnlyList<string> tokens)
    {
        var expanded = new HashSet<string>(tokens, StringComparer.OrdinalIgnoreCase);

        foreach (var (canonical, aliases) in _options.KnowledgeAliases)
        {
            var canonicalTokens = ExtractTokens(canonical);
            if (ContainsAll(expanded, canonicalTokens))
            {
                AddAliasTokens(expanded, aliases);
            }

            foreach (var alias in aliases)
            {
                var aliasTokens = ExtractTokens(alias);
                if (!ContainsAll(expanded, aliasTokens))
                {
                    continue;
                }

                AddTokens(expanded, canonicalTokens);
                AddAliasTokens(expanded, aliases);
                break;
            }
        }

        return expanded.ToList();
    }

    private static bool ContainsAll(HashSet<string> tokens, IReadOnlyList<string> expected)
    {
        return expected.Count > 0 && expected.All(tokens.Contains);
    }

    private static void AddAliasTokens(HashSet<string> tokens, IEnumerable<string> aliases)
    {
        foreach (var alias in aliases)
        {
            AddTokens(tokens, ExtractTokens(alias));
        }
    }

    private static void AddTokens(HashSet<string> tokens, IEnumerable<string> values)
    {
        foreach (var value in values)
        {
            tokens.Add(value);
        }
    }

    private static int Score(string fileName, IReadOnlyList<string> tokens)
    {
        var score = 0;
        foreach (var token in tokens)
        {
            if (fileName.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                score += token.Length <= 3 ? 3 : 2;
            }
        }

        return score;
    }
}
