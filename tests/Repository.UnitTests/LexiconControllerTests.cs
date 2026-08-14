using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;
using Repository.API.Controllers;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Repository.UnitTests.Controllers;

public class LexiconControllerTests
{
    private static LexiconConcept Concept(string key, string? templateScope = null) => new()
    {
        Key = key,
        Label = key,
        ValuePattern = "text",
        TemplateScope = templateScope,
        Aliases = new[] { key }
    };

    private static Mock<ILexiconRepository> RepositoryWithConcepts(params LexiconConcept[] concepts)
    {
        var repository = new Mock<ILexiconRepository>();
        repository
            .Setup(x => x.GetAllConceptsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<LexiconConcept>>.Success(concepts));
        return repository;
    }

    private static LexiconController CreateController(
        Mock<ILexiconRepository>? repository = null,
        Mock<ILexiconProvider>? provider = null,
        Mock<IMetadataRepository>? metadata = null)
        => new(
            (repository ?? new Mock<ILexiconRepository>()).Object,
            (provider ?? new Mock<ILexiconProvider>()).Object,
            metadata?.Object);

    [Fact]
    public async Task GetConcepts_returns_all_concepts_when_no_template()
    {
        var repository = RepositoryWithConcepts(Concept("due_date"), Concept("budget", "RFP"));
        var controller = CreateController(repository);

        var result = await controller.GetConcepts(null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var concepts = Assert.IsAssignableFrom<IReadOnlyList<LexiconConcept>>(ok.Value);
        Assert.Equal(2, concepts.Count);
    }

    [Fact]
    public async Task GetConcepts_filters_to_template_scope_keeping_unscoped()
    {
        var repository = RepositoryWithConcepts(Concept("due_date"), Concept("budget", "RFP"), Concept("page_limit", "Contract"));
        var controller = CreateController(repository);

        var result = await controller.GetConcepts("RFP", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var concepts = Assert.IsAssignableFrom<IReadOnlyList<LexiconConcept>>(ok.Value);
        Assert.Equal(2, concepts.Count);
        Assert.Contains(concepts, c => c.Key == "due_date");
        Assert.Contains(concepts, c => c.Key == "budget");
    }

    [Fact]
    public async Task GetConcepts_returns_server_error_when_load_fails()
    {
        var repository = new Mock<ILexiconRepository>();
        repository
            .Setup(x => x.GetAllConceptsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<LexiconConcept>>.Failure("db unavailable"));
        var controller = CreateController(repository);

        var result = await controller.GetConcepts(null, CancellationToken.None);

        var serverError = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, serverError.StatusCode);
    }

    [Fact]
    public async Task UpsertConcept_invalidates_provider_on_success()
    {
        var repository = new Mock<ILexiconRepository>();
        repository
            .Setup(x => x.UpsertConceptAsync(It.IsAny<LexiconConcept>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        var provider = new Mock<ILexiconProvider>();
        var controller = CreateController(repository, provider);

        var result = await controller.UpsertConcept(Concept("due_date"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        provider.Verify(x => x.Invalidate(), Times.Once);
    }

    [Fact]
    public async Task UpsertConcept_returns_bad_request_when_key_missing()
    {
        var controller = CreateController();

        var result = await controller.UpsertConcept(Concept(""), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task DeleteConcept_invalidates_provider_on_success()
    {
        var repository = new Mock<ILexiconRepository>();
        repository
            .Setup(x => x.DeleteConceptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        var provider = new Mock<ILexiconProvider>();
        var controller = CreateController(repository, provider);

        var result = await controller.DeleteConcept("due_date", CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        provider.Verify(x => x.Invalidate(), Times.Once);
    }

    [Fact]
    public async Task GetUnmappedTerms_returns_pending_terms()
    {
        var terms = new List<UnmappedTerm>
        {
            new() { Term = "novel_concept", SourceId = Guid.NewGuid(), Status = "pending" }
        };
        var repository = new Mock<ILexiconRepository>();
        repository
            .Setup(x => x.GetUnmappedTermsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<UnmappedTerm>>.Success(terms));
        var controller = CreateController(repository);

        var result = await controller.GetUnmappedTerms(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(terms, ok.Value);
    }

    [Fact]
    public async Task ResolveUnmappedTerm_returns_ok_on_success()
    {
        var repository = new Mock<ILexiconRepository>();
        repository
            .Setup(x => x.ResolveUnmappedTermAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        var controller = CreateController(repository);

        var result = await controller.ResolveUnmappedTerm(new ResolveUnmappedTermRequest("novel_concept", Guid.NewGuid()), CancellationToken.None);

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task ResolveUnmappedTerm_returns_bad_request_when_term_missing()
    {
        var controller = CreateController();

        var result = await controller.ResolveUnmappedTerm(new ResolveUnmappedTermRequest("", Guid.NewGuid()), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetGlossary_joins_facts_with_source_names()
    {
        var sourceId = Guid.NewGuid();
        var repository = new Mock<ILexiconRepository>();
        repository
            .Setup(x => x.GetAllConceptsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<LexiconConcept>>.Success(new[] { Concept("due_date") }));
        repository
            .Setup(x => x.GetAllFactsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<DocumentFact>>.Success(new[]
            {
                new DocumentFact
                {
                    SourceId = sourceId,
                    ConceptKey = "due_date",
                    Value = "2022-02-24",
                    SourceSpan = "Proposal Due Date: February 24, 2022",
                    PageNumber = 1,
                    OffsetInPage = 0,
                    Status = "verified"
                }
            }));
        var metadata = new Mock<IMetadataRepository>();
        metadata
            .Setup(x => x.GetByFileIdAsync(sourceId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileMetadata?>.Success(new FileMetadata(new FileDescriptor(sourceId, "CMP 2026 RFP.pdf"), "application/pdf", 100, DateTimeOffset.UtcNow)));
        var controller = CreateController(repository, metadata: metadata);

        var result = await controller.GetGlossary(null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var glossary = Assert.IsType<GlossaryResponse>(ok.Value);
        var fact = Assert.Single(glossary.Facts);
        Assert.Equal("CMP 2026 RFP.pdf", fact.SourceName);
        Assert.Equal("due_date", fact.ConceptKey);
    }

    [Fact]
    public async Task GetGlossary_filters_to_template_scope_keeping_unscoped()
    {
        var repository = new Mock<ILexiconRepository>();
        repository
            .Setup(x => x.GetAllConceptsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<LexiconConcept>>.Success(new[]
            {
                Concept("due_date", "RFP"),
                Concept("budget"),
                Concept("page_limit", "Contract")
            }));
        repository
            .Setup(x => x.GetAllFactsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<DocumentFact>>.Success(new[]
            {
                new DocumentFact { SourceId = Guid.NewGuid(), ConceptKey = "due_date", Value = "2022-02-24", SourceSpan = "s", Status = "verified" },
                new DocumentFact { SourceId = Guid.NewGuid(), ConceptKey = "budget", Value = "$1", SourceSpan = "s", Status = "verified" },
                new DocumentFact { SourceId = Guid.NewGuid(), ConceptKey = "page_limit", Value = "50", SourceSpan = "s", Status = "verified" }
            }));
        var controller = CreateController(repository);

        var result = await controller.GetGlossary("RFP", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var glossary = Assert.IsType<GlossaryResponse>(ok.Value);
        // RFP-scoped + unscoped concepts apply; the Contract-scoped concept and its facts are excluded.
        Assert.Equal(2, glossary.Concepts.Count);
        Assert.Equal(2, glossary.Facts.Count);
        Assert.DoesNotContain(glossary.Facts, f => f.ConceptKey == "page_limit");
    }

    [Fact]
    public async Task ExportGlossary_csv_returns_csv_file()
    {
        var repository = RepositoryWithConcepts(Concept("due_date"));
        repository
            .Setup(x => x.GetAllFactsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<DocumentFact>>.Success(Array.Empty<DocumentFact>()));
        var controller = CreateController(repository);

        var result = await controller.ExportGlossary("csv", null, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("text/csv", file.ContentType);
        Assert.Equal("lexicon-glossary.csv", file.FileDownloadName);
        var content = System.Text.Encoding.UTF8.GetString(file.FileContents);
        Assert.Contains("concept_key,label", content);
        Assert.Contains("due_date", content);
    }

    [Fact]
    public async Task ExportGlossary_json_returns_json_file()
    {
        var repository = RepositoryWithConcepts(Concept("due_date"));
        repository
            .Setup(x => x.GetAllFactsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<DocumentFact>>.Success(Array.Empty<DocumentFact>()));
        var controller = CreateController(repository);

        var result = await controller.ExportGlossary("json", null, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/json", file.ContentType);
        Assert.Equal("lexicon-glossary.json", file.FileDownloadName);
        var content = System.Text.Encoding.UTF8.GetString(file.FileContents);
        Assert.Contains("due_date", content);
    }
}
