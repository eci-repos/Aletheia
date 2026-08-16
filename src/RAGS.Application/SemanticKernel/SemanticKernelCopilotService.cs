using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Configuration;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Aletheia.RAGS.Application.SemanticKernel;

public sealed class SemanticKernelCopilotService : ICopilotService
{
    private readonly IChatService _chatService;
    private readonly IAgentService _agentService;
    private readonly IRagsService _ragsService;
    private readonly IWragsWikiService? _wikiService;
    private readonly IKnowledgeSourceResolver? _knowledgeSourceResolver;
    private readonly IKnowledgeSourceIngestionService? _knowledgeSourceIngestionService;
    private readonly CopilotOptions _options;
    private readonly ChatAgentOptions _chatAgentOptions;
    private readonly IChatAgentInstructionProvider? _instructionProvider;
    private readonly IKnowledgeThemeService? _themeService;

    public SemanticKernelCopilotService(
        IChatService chatService,
        IAgentService agentService,
        IRagsService ragsService,
        IKnowledgeSourceResolver? knowledgeSourceResolver = null,
        IKnowledgeSourceIngestionService? knowledgeSourceIngestionService = null,
        IOptions<CopilotOptions>? options = null,
        IWragsWikiService? wikiService = null,
        IOptions<ChatAgentOptions>? chatAgentOptions = null,
        IChatAgentInstructionProvider? instructionProvider = null,
        IKnowledgeThemeService? themeService = null)
    {
        _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
        _agentService = agentService ?? throw new ArgumentNullException(nameof(agentService));
        _ragsService = ragsService ?? throw new ArgumentNullException(nameof(ragsService));
        _wikiService = wikiService;
        _knowledgeSourceResolver = knowledgeSourceResolver;
        _knowledgeSourceIngestionService = knowledgeSourceIngestionService;
        _options = options?.Value ?? new CopilotOptions();
        _chatAgentOptions = chatAgentOptions?.Value ?? new ChatAgentOptions();
        _instructionProvider = instructionProvider;
        _themeService = themeService;
    }

    public async Task<Result<ChatMessage>> ChatAsync(
        ChatSession session,
        string userMessage,
        ChatRequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (session is null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        if (string.IsNullOrWhiteSpace(userMessage))
        {
            throw new ArgumentException("User message is required.", nameof(userMessage));
        }

        try
        {
            var augmented = await BuildAugmentedMessageAsync(userMessage, options, cancellationToken).ConfigureAwait(false);
            var stopwatch = Stopwatch.StartNew();
            var result = await _chatService.ChatAsync(session, augmented.Prompt, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            if (result.IsSuccess && result.Value is not null)
            {
                result.Value.Stats = BuildCompletionStats(
                    augmented,
                    result.Value.Content,
                    stopwatch.Elapsed);
                result.Value.Citations = RetrievalAugmentedPromptBuilder.BuildCitations(
                    augmented.RetrievalResults,
                    augmented.TopK);
                session.Messages.Add(new ChatMessage { Role = "user", Content = userMessage });
                session.Messages.Add(result.Value);
                session.LastActivity = DateTimeOffset.UtcNow;

                if (session.Messages.Count == 2)
                {
                    session.Title = userMessage.Length > 40 ? userMessage[..40] + "..." : userMessage;
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            return Result<ChatMessage>.Failure($"Chat failed: {ex.Message}");
        }
    }

    private async Task<AugmentedPrompt> BuildAugmentedMessageAsync(
        string userMessage,
        ChatRequestOptions? requestOptions,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<SearchResult>> retrieval;
        KnowledgeSource? source;
        var answerProfile = SelectAnswerProfile(userMessage, requestOptions);
        var orchestrationInstructions = _instructionProvider is not null
            ? await _instructionProvider.GetInstructionsAsync(cancellationToken).ConfigureAwait(false)
            : null;
        var topK = Math.Clamp(_options.RetrievalTopK, 1, 20);
        var retrievalQuery = BuildRetrievalQuery(userMessage, answerProfile);
        if (requestOptions?.RetrievalResults is { Count: > 0 } providedResults)
        {
            var providedTopK = Math.Clamp(Math.Max(topK, providedResults.Count), 1, 50);
            var providedStrategies = DetectRetrievalStrategies(providedResults);
            var providedPrompt = RetrievalAugmentedPromptBuilder.Build(
                userMessage,
                providedResults,
                providedTopK,
                source: null,
                answerProfile: answerProfile,
                defaultAreas: _options.DefaultAreas,
                scopeInstruction: requestOptions.ScopeInstruction,
                sectionOutline: requestOptions.SectionOutline,
                chatAgentOptions: _chatAgentOptions,
                orchestrationInstructions: orchestrationInstructions);

            return new AugmentedPrompt(providedPrompt, providedResults, null, providedTopK, RetrievalUsed: true, providedStrategies);
        }

        try
        {
            source = await ResolveSourceAsync(userMessage, cancellationToken).ConfigureAwait(false);
            var themeSourceIds = await ResolveThemeSourceIdsAsync(requestOptions?.ThemeFilter, cancellationToken).ConfigureAwait(false);
            var sourceIds = ApplyThemeToSourceScope(source?.SourceId, themeSourceIds);
            retrieval = await _ragsService.RetrieveAsync(
                new RetrievalRequest(retrievalQuery, topK, source?.SourceId, sourceIds),
                cancellationToken).ConfigureAwait(false);

            if (source is not null && ShouldHydrateSource(retrieval))
            {
                var ingestion = await _knowledgeSourceIngestionService!
                    .EnsureIngestedAsync(source, cancellationToken)
                    .ConfigureAwait(false);

                if (ingestion.IsSuccess && ingestion.Value)
                {
                    retrieval = await _ragsService.RetrieveAsync(
                        new RetrievalRequest(retrievalQuery, topK, source.SourceId, sourceIds),
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            return AugmentedPrompt.Empty(userMessage, topK);
        }

        if (retrieval.IsFailure || retrieval.Value is null || retrieval.Value.Count == 0)
        {
            if (_chatAgentOptions.BehaviorFlags.RefuseWhenNoContext)
            {
                return AugmentedPrompt.Empty(_chatAgentOptions.NoInformationResponse, topK, source);
            }

            return AugmentedPrompt.Empty(userMessage, topK, source);
        }

        var retrievalResults = await MergeWragsResultsAsync(retrieval.Value, retrievalQuery, topK, cancellationToken).ConfigureAwait(false);
        var strategies = DetectRetrievalStrategies(retrievalResults);
        var prompt = RetrievalAugmentedPromptBuilder.Build(
            userMessage,
            retrievalResults,
            topK,
            source: source,
            answerProfile: answerProfile,
            defaultAreas: _options.DefaultAreas,
            scopeInstruction: requestOptions?.ScopeInstruction,
            sectionOutline: requestOptions?.SectionOutline,
            chatAgentOptions: _chatAgentOptions,
            orchestrationInstructions: orchestrationInstructions);

        return new AugmentedPrompt(prompt, retrievalResults, source, topK, RetrievalUsed: true, strategies);
    }

    /// <summary>Sprint 58: resolves the session theme filter to registered source ids. Null when no filter is active.</summary>
    private async Task<IReadOnlyList<Guid>?> ResolveThemeSourceIdsAsync(
        IReadOnlyList<string>? themes,
        CancellationToken cancellationToken)
    {
        if (themes is not { Count: > 0 } || _themeService is null)
        {
            return null;
        }

        var result = await _themeService
            .ResolveSourceIdsAsync(themes, cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess ? result.Value : null;
    }

    /// <summary>Sprint 58: intersects the resolved single source with the theme scope. Returns the effective source set.</summary>
    private static IReadOnlyList<Guid>? ApplyThemeToSourceScope(Guid? sourceId, IReadOnlyList<Guid>? themeSourceIds)
    {
        if (themeSourceIds is null)
        {
            // No theme filter: preserve the existing single-source (SourceId) scoping untouched.
            return null;
        }

        if (sourceId.HasValue)
        {
            return themeSourceIds.Contains(sourceId.Value)
                ? new List<Guid> { sourceId.Value }
                : new List<Guid>();
        }

        return themeSourceIds;
    }

    private async Task<IReadOnlyList<SearchResult>> MergeWragsResultsAsync(
        IReadOnlyList<SearchResult> retrievalResults,
        string retrievalQuery,
        int topK,
        CancellationToken cancellationToken)
    {
        if (_wikiService is null)
        {
            return retrievalResults;
        }

        try
        {
            var wiki = await _wikiService.RetrieveAsync(
                new WikiSearchRequest
                {
                    Query = retrievalQuery,
                    Mode = "wrags",
                    TopK = Math.Min(3, topK),
                    Expansion = 1
                },
                cancellationToken).ConfigureAwait(false);

            if (wiki.IsFailure || wiki.Value is null || wiki.Value.Count == 0)
            {
                return retrievalResults;
            }

            var wikiResults = wiki.Value
                .Select(result => new SearchResult(
                    result.Chunk,
                    result.Score,
                    result.Citations,
                    result.RankingSignals,
                    string.IsNullOrWhiteSpace(result.RetrievalStrategy) ? "wrags" : result.RetrievalStrategy,
                    result.Rank))
                .ToList();

            return wikiResults
                .Concat(retrievalResults)
                .GroupBy(result => result.Chunk.Id)
                .Select(group => group.OrderByDescending(result => result.Score).First())
                .OrderByDescending(result => result.Score)
                .Take(topK)
                .ToList();
        }
        catch
        {
            return retrievalResults;
        }
    }

    private static IReadOnlyList<string> DetectRetrievalStrategies(IReadOnlyList<SearchResult> results)
    {
        var strategies = results
            .Select(r => r.RetrievalStrategy)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return strategies.Count == 0
            ? new[] { "none" }
            : strategies;
    }

    private static ChatCompletionStats BuildCompletionStats(
        AugmentedPrompt augmented,
        string assistantContent,
        TimeSpan elapsed)
    {
        var elapsedSeconds = Math.Max(elapsed.TotalSeconds, 0.001d);
        var retrievalResults = augmented.RetrievalResults;
        var completionTokens = EstimateTokens(assistantContent);
        var promptTokens = EstimateTokens(augmented.Prompt);
        var citationCount = retrievalResults.Sum(result => result.Citations.Count);
        var maxScore = retrievalResults.Count == 0 ? 0d : retrievalResults.Max(result => result.Score);
        var averageScore = retrievalResults.Count == 0 ? 0d : retrievalResults.Average(result => result.Score);
        var citationBoost = retrievalResults.Count == 0 ? 0d : Math.Min(0.2d, citationCount * 0.04d);
        var contextBoost = retrievalResults.Count == 0 ? 0d : Math.Min(0.15d, retrievalResults.Count / (double)Math.Max(augmented.TopK, 1) * 0.15d);
        var hasInternalContext = retrievalResults.Count > 0;
        var hasCitations = citationCount > 0;
        var confidence = hasInternalContext
            ? Math.Clamp((averageScore * 0.55d) + citationBoost + contextBoost + (hasCitations ? 0.15d : 0d), 0d, 0.98d)
            : 0d;
        var strategyList = augmented.RetrievalStrategies.Count > 0
            ? string.Join(", ", augmented.RetrievalStrategies)
            : "none";

        return new ChatCompletionStats
        {
            ElapsedSeconds = Math.Round(elapsedSeconds, 3),
            EstimatedPromptTokens = promptTokens,
            EstimatedCompletionTokens = completionTokens,
            TokensPerSecond = Math.Round(completionTokens / elapsedSeconds, 2),
            RetrievedContextCount = retrievalResults.Count,
            CitationCount = citationCount,
            MaxRetrievalScore = Math.Round(maxScore, 4),
            AverageRetrievalScore = Math.Round(averageScore, 4),
            AlignmentConfidence = Math.Round(confidence, 3),
            ConfidenceBasis = hasInternalContext
                ? $"Heuristic based on {strategyList} retrieval score, retrieved context count, and {citationCount} internal citation(s)."
                : "No retrieval context was available; confidence is not evidence-backed."
        };
    }

    private static int EstimateTokens(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var wordEstimate = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length * 1.33d;
        var characterEstimate = value.Length / 4d;
        return Math.Max(1, (int)Math.Round(Math.Max(wordEstimate, characterEstimate)));
    }

    private bool ShouldHydrateSource(Result<IReadOnlyList<SearchResult>> retrieval)
    {
        return _knowledgeSourceIngestionService is not null
            && (retrieval.IsFailure || retrieval.Value is null || retrieval.Value.Count == 0);
    }

    private async Task<KnowledgeSource?> ResolveSourceAsync(string userMessage, CancellationToken cancellationToken)
    {
        if (_knowledgeSourceResolver is null)
        {
            return null;
        }

        var result = await _knowledgeSourceResolver.ResolveAsync(userMessage, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? result.Value : null;
    }

    private CopilotAnswerProfileOptions? SelectAnswerProfile(string userMessage, ChatRequestOptions? requestOptions)
    {
        var outputFormat = ResolveOutputFormat(requestOptions?.OutputFormat);
        CopilotAnswerProfileOptions? profile = null;

        if (_options.AnswerProfiles.Count > 0)
        {
            profile = _options.AnswerProfiles.Values.FirstOrDefault(candidate =>
                candidate.MatchTerms.Any(term => !string.IsNullOrWhiteSpace(term)
                    && userMessage.Contains(term, StringComparison.OrdinalIgnoreCase)));

            if (profile is null
                && !string.IsNullOrWhiteSpace(_options.DefaultAnswerProfile)
                && _options.AnswerProfiles.TryGetValue(_options.DefaultAnswerProfile, out var defaultProfile))
            {
                profile = defaultProfile;
            }
        }

        return outputFormat is null
            ? profile
            : WithOutputFormat(profile, outputFormat);
    }

    private string BuildRetrievalQuery(string userMessage, CopilotAnswerProfileOptions? answerProfile)
    {
        var areas = answerProfile?.Areas is { Count: > 0 }
            ? answerProfile.Areas
            : _options.DefaultAreas;

        var normalizedAreas = areas
            .Where(area => !string.IsNullOrWhiteSpace(area))
            .Select(area => area.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return normalizedAreas.Count == 0
            ? userMessage
            : $"{userMessage}\nFocus areas: {string.Join(", ", normalizedAreas)}";
    }

    private static string? ResolveOutputFormat(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "auto" => null,
            "summary" => "Markdown with a concise summary, key findings, and citations",
            "table" => "Markdown table with columns: Area, Requirement, Evidence, Citation",
            "bullets" => "Markdown bullet list grouped by area with citations",
            "json" => "valid JSON with keys summary and requirements; requirements must include area, requirement, evidence, and citations",
            _ => null
        };
    }

    private CopilotAnswerProfileOptions WithOutputFormat(CopilotAnswerProfileOptions? profile, string outputFormat)
    {
        return new CopilotAnswerProfileOptions
        {
            MatchTerms = profile?.MatchTerms.ToList() ?? new List<string>(),
            Areas = profile?.Areas.ToList() ?? _options.DefaultAreas.ToList(),
            OutputFormat = outputFormat,
            RequireCitations = profile?.RequireCitations ?? true,
            Instructions = profile?.Instructions.ToList() ?? new List<string>()
        };
    }

    public Task<Result<SummaryResponse>> SummarizeAsync(SummaryRequest request, CancellationToken cancellationToken = default)
    {
        return _agentService.SummarizeAsync(request, cancellationToken);
    }

    public Task<Result<ExplanationResponse>> ExplainAsync(ExplanationRequest request, CancellationToken cancellationToken = default)
    {
        return _agentService.ExplainAsync(request, cancellationToken);
    }

    public Task<Result<DiscoveryResponse>> DiscoverAsync(DiscoveryRequest request, CancellationToken cancellationToken = default)
    {
        return _agentService.DiscoverAsync(request, cancellationToken);
    }

    private sealed record AugmentedPrompt(
        string Prompt,
        IReadOnlyList<SearchResult> RetrievalResults,
        KnowledgeSource? Source,
        int TopK,
        bool RetrievalUsed,
        IReadOnlyList<string> RetrievalStrategies)
    {
        public static AugmentedPrompt Empty(string prompt, int topK, KnowledgeSource? source = null)
        {
            return new AugmentedPrompt(prompt, Array.Empty<SearchResult>(), source, topK, RetrievalUsed: false, new[] { "none" });
        }
    }
}
