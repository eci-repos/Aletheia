using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Configuration;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.RAGS.Application.SemanticKernel;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;
using Microsoft.Extensions.Options;

namespace RAGS.UnitTests;

public class SemanticKernelCopilotServiceTests
{
    [Fact]
    public async Task ChatAsync_augments_message_with_ranked_cited_rag_context()
    {
        var sourceId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();
        var chatService = new CapturingChatService();
        var ragsService = new FakeRagsService(new[]
        {
            new SearchResult(
                new Chunk(chunkId, sourceId, "CMP analysis content from the uploaded document.", 3),
                0.91f,
                new[] { "CMP 2022 - 3. RFP Analysis.docx" },
                new Dictionary<string, float> { ["semantic"] = 0.91f },
                "semantic",
                1)
        });
        var service = new SemanticKernelCopilotService(chatService, new FakeAgentService(), ragsService);
        var session = new ChatSession();

        var result = await service.ChatAsync(session, "What does CMP say?");

        Assert.True(result.IsSuccess);
        Assert.Contains("You are an agent of the Aletheia platform.", chatService.LastUserMessage);
        Assert.Contains("Do not use general internet knowledge", chatService.LastUserMessage);
        Assert.Contains("Retrieved context:", chatService.LastUserMessage);
        Assert.Contains("[1] Rank: 1; Score: 0.91; Strategy: semantic", chatService.LastUserMessage);
        Assert.Contains($"SourceId: {sourceId}; ChunkId: {chunkId}; ChunkIndex: 3", chatService.LastUserMessage);
        Assert.Contains("Citations: CMP 2022 - 3. RFP Analysis.docx", chatService.LastUserMessage);
        Assert.Contains("User question:", chatService.LastUserMessage);
        Assert.Equal("What does CMP say?", session.Messages[0].Content);
        Assert.NotNull(result.Value!.Stats);
        Assert.Equal(1, result.Value.Stats.RetrievedContextCount);
        Assert.Equal(1, result.Value.Stats.CitationCount);
        Assert.True(result.Value.Stats.ElapsedSeconds > 0);
        Assert.True(result.Value.Stats.TokensPerSecond > 0);
        Assert.True(result.Value.Stats.AlignmentConfidence > 0);
        Assert.Contains("semantic", result.Value.Stats.ConfidenceBasis);
    }

    [Fact]
    public async Task ChatAsync_uses_original_message_when_retrieval_fails()
    {
        var chatService = new CapturingChatService();
        var service = new SemanticKernelCopilotService(chatService, new FakeAgentService(), FakeRagsService.Failing());

        var result = await service.ChatAsync(new ChatSession(), "plain chat");

        Assert.True(result.IsSuccess);
        Assert.Equal("plain chat", chatService.LastUserMessage);
        Assert.NotNull(result.Value!.Stats);
        Assert.Equal(0, result.Value.Stats.RetrievedContextCount);
        Assert.Equal(0, result.Value.Stats.AlignmentConfidence);
        Assert.Contains("No retrieval context was available", result.Value.Stats.ConfidenceBasis);
    }

    [Fact]
    public async Task ChatAsync_filters_retrieval_to_resolved_knowledge_source()
    {
        var sourceId = Guid.NewGuid();
        var chatService = new CapturingChatService();
        var ragsService = new FakeRagsService(new[]
        {
            new SearchResult(
                new Chunk(Guid.NewGuid(), sourceId, "Activities must include stakeholder workshops.", 0),
                0.88f,
                new[] { "CMP Final RFP.docx" })
        });
        var sourceResolver = new FakeKnowledgeSourceResolver(new KnowledgeSource(
            sourceId,
            "CMP Final RFP.docx",
            DateTimeOffset.UtcNow));
        var service = new SemanticKernelCopilotService(
            chatService,
            new FakeAgentService(),
            ragsService,
            sourceResolver,
            options: Options.Create(new CopilotOptions
            {
                DefaultAreas = new() { "activities", "requirements" },
                DefaultAnswerProfile = "rfp_requirements",
                AnswerProfiles =
                {
                    ["rfp_requirements"] = new CopilotAnswerProfileOptions
                    {
                        MatchTerms = new() { "rfp" },
                        Areas = new() { "activities", "requirements" },
                        OutputFormat = "Markdown table"
                    }
                }
            }));

        var result = await service.ChatAsync(new ChatSession(), "what requirements are defined in the last CMP RFP related to activities?");

        Assert.True(result.IsSuccess);
        Assert.Equal(sourceId, ragsService.LastRetrievalRequest?.SourceId);
        Assert.Contains("Focus areas: activities, requirements", ragsService.LastRetrievalRequest?.Query);
        Assert.Contains("Format the answer as Markdown table.", chatService.LastUserMessage);
        Assert.Contains("Activities must include stakeholder workshops.", chatService.LastUserMessage);
    }

    [Fact]
    public async Task ChatAsync_applies_requested_output_format_override()
    {
        var chatService = new CapturingChatService();
        var ragsService = new FakeRagsService(new[]
        {
            new SearchResult(
                new Chunk(Guid.NewGuid(), Guid.NewGuid(), "Activities must include stakeholder workshops.", 0),
                0.88f,
                new[] { "CMP Final RFP.docx" })
        });
        var service = new SemanticKernelCopilotService(
            chatService,
            new FakeAgentService(),
            ragsService,
            options: Options.Create(new CopilotOptions
            {
                DefaultAreas = new() { "activities" }
            }));

        var result = await service.ChatAsync(
            new ChatSession(),
            "what activities are required?",
            new ChatRequestOptions { OutputFormat = "bullets" });

        Assert.True(result.IsSuccess);
        Assert.Contains("Format the answer as Markdown bullet list grouped by area with citations.", chatService.LastUserMessage);
    }

    [Fact]
    public async Task ChatAsync_ingests_resolved_source_when_retrieval_is_empty()
    {
        var sourceId = Guid.NewGuid();
        var chatService = new CapturingChatService();
        var ragsService = new FakeRagsService(Array.Empty<SearchResult>());
        var source = new KnowledgeSource(sourceId, "CMP Final RFP.docx", DateTimeOffset.UtcNow);
        var sourceResolver = new FakeKnowledgeSourceResolver(source);
        var sourceIngestion = new FakeKnowledgeSourceIngestionService(sourceToIngest =>
        {
            ragsService.Results = new[]
            {
                new SearchResult(
                    new Chunk(Guid.NewGuid(), sourceToIngest.SourceId, "Activities must include stakeholder workshops.", 0),
                    0.88f,
                    new[] { sourceToIngest.SourceName })
            };

            return true;
        });
        var service = new SemanticKernelCopilotService(
            chatService,
            new FakeAgentService(),
            ragsService,
            sourceResolver,
            sourceIngestion);

        var result = await service.ChatAsync(new ChatSession(), "what activities are in the last Cleveland Metroparks RFP?");

        Assert.True(result.IsSuccess);
        Assert.Equal(sourceId, sourceIngestion.LastSource?.SourceId);
        Assert.Contains("Resolved KB artifact: CMP Final RFP.docx", chatService.LastUserMessage);
        Assert.Contains("Activities must include stakeholder workshops.", chatService.LastUserMessage);
    }


    [Fact]
    public async Task MetadataKnowledgeSourceResolver_selects_most_recent_matching_source()
    {
        var olderId = Guid.NewGuid();
        var newerId = Guid.NewGuid();
        var resolver = new MetadataKnowledgeSourceResolver(
            new FakeMetadataRepository(new[]
        {
            new FileMetadata(new FileDescriptor(olderId, "CMP Draft RFP.docx"), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 100, DateTimeOffset.UtcNow.AddDays(-10)),
            new FileMetadata(new FileDescriptor(newerId, "CMP Final RFP.docx"), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 100, DateTimeOffset.UtcNow.AddDays(-1)),
            new FileMetadata(new FileDescriptor(Guid.NewGuid(), "Unrelated Proposal.docx"), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 100, DateTimeOffset.UtcNow)
        }),
            Options.Create(new CopilotOptions
            {
                KnowledgeAliases =
                {
                    ["cmp"] = new[] { "Cleveland Metroparks" }
                }
            }));

        var result = await resolver.ResolveAsync("what requirements are defined in the last Cleveland Metroparks RFP related to activities?");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(newerId, result.Value.SourceId);
        Assert.Equal("CMP Final RFP.docx", result.Value.SourceName);
    }

    [Fact]
    public void RetrievalAugmentedPromptBuilder_orders_by_rank_and_trims_content()
    {
        var first = new SearchResult(
            new Chunk(Guid.NewGuid(), Guid.NewGuid(), "second by rank", 0),
            0.99f,
            rank: 2);
        var second = new SearchResult(
            new Chunk(Guid.NewGuid(), Guid.NewGuid(), "first by rank but very long", 1),
            0.75f,
            rank: 1);

        var prompt = RetrievalAugmentedPromptBuilder.Build(
            "question",
            new[] { first, second },
            maxResults: 2,
            maxChunkCharacters: 10);

        Assert.True(prompt.IndexOf("[1] Rank: 1", StringComparison.Ordinal) < prompt.IndexOf("[2] Rank: 2", StringComparison.Ordinal));
        Assert.Contains("first by r...", prompt);
        Assert.Contains("You are an agent of the Aletheia platform", prompt);
        Assert.Contains("Do not use general internet knowledge", prompt);
        Assert.Contains("question", prompt);
    }

    [Fact]
    public void RetrievalAugmentedPromptBuilder_applies_configured_output_profile()
    {
        var prompt = RetrievalAugmentedPromptBuilder.Build(
            "question",
            new[]
            {
                new SearchResult(
                    new Chunk(Guid.NewGuid(), Guid.NewGuid(), "content", 0),
                    0.75f,
                    rank: 1)
            },
            answerProfile: new CopilotAnswerProfileOptions
            {
                Areas = new() { "activities", "pricing" },
                OutputFormat = "Markdown table",
                Instructions = new() { "Mark missing areas as not found." }
            });

        Assert.Contains("Cover these configured focus areas when relevant: activities, pricing.", prompt);
        Assert.Contains("Format the answer as Markdown table.", prompt);
        Assert.Contains("Mark missing areas as not found.", prompt);
    }

    private sealed class CapturingChatService : IChatService
    {
        public string LastUserMessage { get; private set; } = string.Empty;

        public Task<Result<ChatMessage>> ChatAsync(ChatSession session, string userMessage, CancellationToken cancellationToken = default)
        {
            LastUserMessage = userMessage;
            return Task.FromResult(Result<ChatMessage>.Success(new ChatMessage { Role = "assistant", Content = "answer" }));
        }
    }

    private sealed class FakeRagsService : IRagsService
    {
        private readonly bool _throwOnRetrieve;

        public IReadOnlyList<SearchResult> Results { get; set; }

        public RetrievalRequest? LastRetrievalRequest { get; private set; }

        public FakeRagsService(IReadOnlyList<SearchResult> results, bool throwOnRetrieve = false)
        {
            Results = results;
            _throwOnRetrieve = throwOnRetrieve;
        }

        public static FakeRagsService Failing() => new(Array.Empty<SearchResult>(), true);

        public Task<Result> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }

        public Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken = default)
        {
            LastRetrievalRequest = request;
            if (_throwOnRetrieve)
            {
                throw new InvalidOperationException("retrieval failed");
            }

            return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(Results));
        }
    }

    private sealed class FakeKnowledgeSourceResolver : IKnowledgeSourceResolver
    {
        private readonly KnowledgeSource? _source;

        public FakeKnowledgeSourceResolver(KnowledgeSource? source)
        {
            _source = source;
        }

        public Task<Result<KnowledgeSource?>> ResolveAsync(string userMessage, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<KnowledgeSource?>.Success(_source));
        }
    }

    private sealed class FakeKnowledgeSourceIngestionService : IKnowledgeSourceIngestionService
    {
        private readonly Func<KnowledgeSource, bool> _ingest;

        public FakeKnowledgeSourceIngestionService(Func<KnowledgeSource, bool> ingest)
        {
            _ingest = ingest;
        }

        public KnowledgeSource? LastSource { get; private set; }

        public Task<Result<bool>> EnsureIngestedAsync(KnowledgeSource source, CancellationToken cancellationToken = default)
        {
            LastSource = source;
            return Task.FromResult(Result<bool>.Success(_ingest(source)));
        }
    }

    private sealed class FakeMetadataRepository : IMetadataRepository
    {
        private readonly IReadOnlyList<FileMetadata> _items;

        public FakeMetadataRepository(IReadOnlyList<FileMetadata> items)
        {
            _items = items;
        }

        public Task<Result<FileMetadata>> GetAsync(FileDescriptor descriptor, CancellationToken cancellationToken = default)
        {
            var item = _items.FirstOrDefault(metadata => metadata.Descriptor.FileId == descriptor.FileId);
            return Task.FromResult(item is null
                ? Result<FileMetadata>.Failure("not found")
                : Result<FileMetadata>.Success(item));
        }

        public Task<Result<FileMetadata>> SaveAsync(FileMetadata metadata, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<FileMetadata>.Success(metadata));
        }

        public Task<Result<PagedResult<FileMetadata>>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<PagedResult<FileMetadata>>.Success(new PagedResult<FileMetadata>(
                _items,
                request.PageNumber,
                request.PageSize,
                _items.Count)));
        }

        public Task<Result> DeleteAsync(FileDescriptor descriptor, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class FakeAgentService : IAgentService
    {
        public Task<Result<SummaryResponse>> SummarizeAsync(SummaryRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<SummaryResponse>.Success(new SummaryResponse()));
        }

        public Task<Result<ExplanationResponse>> ExplainAsync(ExplanationRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<ExplanationResponse>.Success(new ExplanationResponse()));
        }

        public Task<Result<DiscoveryResponse>> DiscoverAsync(DiscoveryRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<DiscoveryResponse>.Success(new DiscoveryResponse()));
        }
    }
}
