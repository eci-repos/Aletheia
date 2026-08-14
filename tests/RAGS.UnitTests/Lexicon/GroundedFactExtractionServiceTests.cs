using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.RAGS.Application.Lexicon;

namespace RAGS.UnitTests;

public class GroundedFactExtractionServiceTests
{
    [Fact]
    public async Task ExtractAsync_proposes_verifies_and_persists_facts()
    {
        var repository = new FakeLexiconRepository(LexiconSeedData.Defaults);
        var proposer = new FakeProposer(new[]
        {
            new ProposedFact
            {
                ConceptHint = "due_date",
                Value = "February 24, 2022",
                SourceSpan = "Proposal Due Date: February 24, 2022"
            }
        });
        var service = new GroundedFactExtractionService(repository, proposer);
        var sourceId = Guid.NewGuid();
        const string text = "Proposal Due Date: February 24, 2022";

        var result = await service.ExtractAsync(sourceId, text, null);

        Assert.True(result.IsSuccess);
        var fact = Assert.Single(result.Value!);
        Assert.Equal("due_date", fact.ConceptKey);
        Assert.Equal(sourceId, fact.SourceId);
        Assert.Single(repository.SavedFacts);
    }

    [Fact]
    public async Task ExtractAsync_records_unmapped_concept_hints()
    {
        var repository = new FakeLexiconRepository(LexiconSeedData.Defaults);
        var proposer = new FakeProposer(new[]
        {
            new ProposedFact
            {
                ConceptHint = "novel_concept",
                Value = "some value",
                SourceSpan = "Novel term: some value"
            }
        });
        var service = new GroundedFactExtractionService(repository, proposer);
        var sourceId = Guid.NewGuid();

        var result = await service.ExtractAsync(sourceId, "Novel term: some value", null);

        Assert.True(result.IsSuccess);
        Assert.Contains("novel_concept", repository.UnmappedTerms);
    }

    [Fact]
    public async Task ExtractAsync_does_not_record_known_concept_as_unmapped()
    {
        var repository = new FakeLexiconRepository(LexiconSeedData.Defaults);
        var proposer = new FakeProposer(new[]
        {
            new ProposedFact
            {
                ConceptHint = "due_date",
                Value = "February 24, 2022",
                SourceSpan = "Proposal Due Date: February 24, 2022"
            }
        });
        var service = new GroundedFactExtractionService(repository, proposer);

        await service.ExtractAsync(Guid.NewGuid(), "Proposal Due Date: February 24, 2022", null);

        Assert.DoesNotContain("due_date", repository.UnmappedTerms);
    }

    [Fact]
    public async Task ExtractAsync_returns_failure_when_lexicon_load_fails()
    {
        var repository = new FakeLexiconRepository(LexiconSeedData.Defaults) { FailLoad = true };
        var proposer = new FakeProposer(Array.Empty<ProposedFact>());
        var service = new GroundedFactExtractionService(repository, proposer);

        var result = await service.ExtractAsync(Guid.NewGuid(), "text", null);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task ExtractAsync_returns_empty_when_no_proposals()
    {
        var repository = new FakeLexiconRepository(LexiconSeedData.Defaults);
        var proposer = new FakeProposer(Array.Empty<ProposedFact>());
        var service = new GroundedFactExtractionService(repository, proposer);

        var result = await service.ExtractAsync(Guid.NewGuid(), "text", null);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
        Assert.Empty(repository.SavedFacts);
    }

    private sealed class FakeLexiconRepository : ILexiconRepository
    {
        private readonly IReadOnlyList<LexiconConcept> _concepts;

        public FakeLexiconRepository(IReadOnlyList<LexiconConcept> concepts) => _concepts = concepts;

        public bool FailLoad { get; set; }
        public List<DocumentFact> SavedFacts { get; } = new();
        public List<string> UnmappedTerms { get; } = new();

        public Task<Result<IReadOnlyList<LexiconConcept>>> GetAllConceptsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(FailLoad
                ? Result<IReadOnlyList<LexiconConcept>>.Failure("lexicon load failed")
                : Result<IReadOnlyList<LexiconConcept>>.Success(_concepts));

        public Task<Result> UpsertConceptAsync(LexiconConcept concept, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success());

        public Task<Result> SaveFactsAsync(Guid sourceId, IReadOnlyList<DocumentFact> facts, CancellationToken cancellationToken = default)
        {
            SavedFacts.AddRange(facts);
            return Task.FromResult(Result.Success());
        }

        public Task<Result<IReadOnlyList<DocumentFact>>> GetFactsAsync(Guid sourceId, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<IReadOnlyList<DocumentFact>>.Success(SavedFacts));

        public Task<Result> RecordUnmappedTermAsync(string term, Guid sourceId, CancellationToken cancellationToken = default)
        {
            UnmappedTerms.Add(term);
            return Task.FromResult(Result.Success());
        }

        public Task<Result<IReadOnlyList<UnmappedTerm>>> GetUnmappedTermsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Result<IReadOnlyList<UnmappedTerm>>.Success(Array.Empty<UnmappedTerm>()));
    }

    private sealed class FakeProposer : IFactProposer
    {
        private readonly IReadOnlyList<ProposedFact> _proposals;

        public FakeProposer(IReadOnlyList<ProposedFact> proposals) => _proposals = proposals;

        public Task<Result<IReadOnlyList<ProposedFact>>> ProposeAsync(
            string text,
            IReadOnlyList<LexiconConcept> concepts,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Result<IReadOnlyList<ProposedFact>>.Success(_proposals));
    }
}
