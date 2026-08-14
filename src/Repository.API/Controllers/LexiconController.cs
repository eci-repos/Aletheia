using System.Text;
using System.Text.Json;
using Aletheia.Foundation.Security;
using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.Repository.Abstractions.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Repository.API.Controllers;

/// <summary>
/// Sprint 71: the lexicon governance + glossary surface. Read endpoints (concepts, glossary, export)
/// are available to any authenticated user; write endpoints (upsert/delete concepts, resolve unmapped
/// terms) are Administrator-only. Admin writes invalidate the <c>LexiconProvider</c> cache so edits
/// take effect on the next retrieval read. The glossary joins <c>document_facts</c> with Repository
/// metadata for source names — the controller hosts both modules, so no cross-module dependency.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class LexiconController : ControllerBase
{
    private readonly ILexiconRepository _lexiconRepository;
    private readonly ILexiconProvider _lexiconProvider;
    private readonly IMetadataRepository? _metadataRepository;

    public LexiconController(
        ILexiconRepository lexiconRepository,
        ILexiconProvider lexiconProvider,
        IMetadataRepository? metadataRepository = null)
    {
        _lexiconRepository = lexiconRepository ?? throw new ArgumentNullException(nameof(lexiconRepository));
        _lexiconProvider = lexiconProvider ?? throw new ArgumentNullException(nameof(lexiconProvider));
        _metadataRepository = metadataRepository;
    }

    /// <summary>All concepts (optionally filtered to a template scope; unscoped concepts always included).</summary>
    [HttpGet("concepts")]
    public async Task<ActionResult<IReadOnlyList<LexiconConcept>>> GetConcepts(
        [FromQuery] string? template,
        CancellationToken cancellationToken)
    {
        var result = await _lexiconRepository.GetAllConceptsAsync(cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return StatusCode(500, new { error = result.Error });
        }

        var concepts = result.Value ?? Array.Empty<LexiconConcept>();
        if (!string.IsNullOrWhiteSpace(template))
        {
            concepts = concepts
                .Where(c => string.IsNullOrWhiteSpace(c.TemplateScope)
                    || string.Equals(c.TemplateScope, template, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return Ok(concepts);
    }

    /// <summary>Create or replace a concept (label, value pattern, template scope, full alias set).</summary>
    [HttpPut("concepts")]
    [Authorize(Roles = RoleDefinitions.Administrator)]
    public async Task<IActionResult> UpsertConcept([FromBody] LexiconConcept concept, CancellationToken cancellationToken)
    {
        if (concept is null || string.IsNullOrWhiteSpace(concept.Key))
        {
            return BadRequest(new { error = "Concept key is required." });
        }

        var result = await _lexiconRepository.UpsertConceptAsync(concept, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        _lexiconProvider.Invalidate();
        return Ok(concept);
    }

    /// <summary>Delete a concept (aliases cascade; existing facts keep their concept_key as a label).</summary>
    [HttpDelete("concepts/{key}")]
    [Authorize(Roles = RoleDefinitions.Administrator)]
    public async Task<IActionResult> DeleteConcept(string key, CancellationToken cancellationToken)
    {
        var result = await _lexiconRepository.DeleteConceptAsync(key, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        _lexiconProvider.Invalidate();
        return NoContent();
    }

    /// <summary>Pending (unreviewed) unmapped terms for the admin governance surface.</summary>
    [HttpGet("unmapped")]
    [Authorize(Roles = RoleDefinitions.Administrator)]
    public async Task<ActionResult<IReadOnlyList<UnmappedTerm>>> GetUnmappedTerms(CancellationToken cancellationToken)
    {
        var result = await _lexiconRepository.GetUnmappedTermsAsync(cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    /// <summary>Mark an unmapped term resolved (confirmed as an alias or dismissed) so it leaves the review queue.</summary>
    [HttpPost("unmapped/resolve")]
    [Authorize(Roles = RoleDefinitions.Administrator)]
    public async Task<IActionResult> ResolveUnmappedTerm([FromBody] ResolveUnmappedTermRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Term))
        {
            return BadRequest(new { error = "Term is required." });
        }

        var result = await _lexiconRepository
            .ResolveUnmappedTermAsync(request.Term, request.SourceId, cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess ? Ok() : BadRequest(new { error = result.Error });
    }

    /// <summary>End-user glossary: concepts + verified facts (with source names), optionally scoped to a template.</summary>
    [HttpGet("glossary")]
    public async Task<ActionResult<GlossaryResponse>> GetGlossary(
        [FromQuery] string? template,
        CancellationToken cancellationToken)
    {
        var conceptsResult = await _lexiconRepository.GetAllConceptsAsync(cancellationToken).ConfigureAwait(false);
        if (conceptsResult.IsFailure)
        {
            return StatusCode(500, new { error = conceptsResult.Error });
        }

        var factsResult = await _lexiconRepository.GetAllFactsAsync(cancellationToken).ConfigureAwait(false);
        if (factsResult.IsFailure)
        {
            return StatusCode(500, new { error = factsResult.Error });
        }

        var concepts = conceptsResult.Value ?? Array.Empty<LexiconConcept>();
        var facts = factsResult.Value ?? Array.Empty<DocumentFact>();
        if (!string.IsNullOrWhiteSpace(template))
        {
            concepts = concepts
                .Where(c => string.IsNullOrWhiteSpace(c.TemplateScope)
                    || string.Equals(c.TemplateScope, template, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var scopedKeys = new HashSet<string>(concepts.Select(c => c.Key), StringComparer.OrdinalIgnoreCase);
            facts = facts.Where(f => scopedKeys.Contains(f.ConceptKey)).ToList();
        }

        var sourceNames = await ResolveSourceNamesAsync(facts.Select(f => f.SourceId).Distinct(), cancellationToken).ConfigureAwait(false);
        var glossaryFacts = facts.Select(f => new GlossaryFact
        {
            SourceId = f.SourceId,
            SourceName = sourceNames.TryGetValue(f.SourceId, out var name) ? name : f.SourceId.ToString(),
            ConceptKey = f.ConceptKey,
            Value = f.Value,
            SourceSpan = f.SourceSpan,
            PageNumber = f.PageNumber,
            OffsetInPage = f.OffsetInPage
        }).ToList();

        return Ok(new GlossaryResponse { Concepts = concepts, Facts = glossaryFacts });
    }

    /// <summary>Download the glossary as CSV or JSON (concepts + aliases + facts).</summary>
    [HttpGet("glossary/export")]
    public async Task<IActionResult> ExportGlossary(
        [FromQuery] string format = "json",
        [FromQuery] string? template = null,
        CancellationToken cancellationToken = default)
    {
        var glossaryResult = await BuildGlossaryAsync(template, cancellationToken).ConfigureAwait(false);
        if (glossaryResult.IsFailure)
        {
            return StatusCode(500, new { error = glossaryResult.Error });
        }

        var glossary = glossaryResult.Value!;
        var normalizedFormat = string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase) ? "csv" : "json";
        if (normalizedFormat == "csv")
        {
            var csv = BuildCsv(glossary);
            return File(Encoding.UTF8.GetBytes(csv), "text/csv", "lexicon-glossary.csv");
        }

        var json = JsonSerializer.Serialize(glossary, new JsonSerializerOptions { WriteIndented = true });
        return File(Encoding.UTF8.GetBytes(json), "application/json", "lexicon-glossary.json");
    }

    private async Task<Result<GlossaryResponse>> BuildGlossaryAsync(string? template, CancellationToken cancellationToken)
    {
        var conceptsResult = await _lexiconRepository.GetAllConceptsAsync(cancellationToken).ConfigureAwait(false);
        if (conceptsResult.IsFailure)
        {
            return Result<GlossaryResponse>.Failure(conceptsResult.Error ?? "Lexicon load failed.");
        }

        var factsResult = await _lexiconRepository.GetAllFactsAsync(cancellationToken).ConfigureAwait(false);
        if (factsResult.IsFailure)
        {
            return Result<GlossaryResponse>.Failure(factsResult.Error ?? "Fact load failed.");
        }

        var concepts = conceptsResult.Value ?? Array.Empty<LexiconConcept>();
        var facts = factsResult.Value ?? Array.Empty<DocumentFact>();
        if (!string.IsNullOrWhiteSpace(template))
        {
            concepts = concepts
                .Where(c => string.IsNullOrWhiteSpace(c.TemplateScope)
                    || string.Equals(c.TemplateScope, template, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var scopedKeys = new HashSet<string>(concepts.Select(c => c.Key), StringComparer.OrdinalIgnoreCase);
            facts = facts.Where(f => scopedKeys.Contains(f.ConceptKey)).ToList();
        }

        var sourceNames = await ResolveSourceNamesAsync(facts.Select(f => f.SourceId).Distinct(), cancellationToken).ConfigureAwait(false);
        var glossaryFacts = facts.Select(f => new GlossaryFact
        {
            SourceId = f.SourceId,
            SourceName = sourceNames.TryGetValue(f.SourceId, out var name) ? name : f.SourceId.ToString(),
            ConceptKey = f.ConceptKey,
            Value = f.Value,
            SourceSpan = f.SourceSpan,
            PageNumber = f.PageNumber,
            OffsetInPage = f.OffsetInPage
        }).ToList();

        return Result<GlossaryResponse>.Success(new GlossaryResponse { Concepts = concepts, Facts = glossaryFacts });
    }

    private async Task<IReadOnlyDictionary<Guid, string>> ResolveSourceNamesAsync(
        IEnumerable<Guid> sourceIds,
        CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string>();
        if (_metadataRepository is null)
        {
            return names;
        }

        foreach (var sourceId in sourceIds.Distinct())
        {
            var metadata = await _metadataRepository.GetByFileIdAsync(sourceId, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (metadata.IsSuccess && metadata.Value is not null)
            {
                names[sourceId] = metadata.Value.Descriptor.FileName;
            }
        }

        return names;
    }

    private static string BuildCsv(GlossaryResponse glossary)
    {
        var builder = new StringBuilder();
        builder.AppendLine("concept_key,label,value_pattern,template_scope,alias,source_name,source_id,value,page_number");

        foreach (var concept in glossary.Concepts)
        {
            var aliases = concept.Aliases is { Count: > 0 } ? concept.Aliases : new[] { string.Empty };
            foreach (var alias in aliases)
            {
                builder.AppendLine(string.Join(",",
                    CsvEscape(concept.Key),
                    CsvEscape(concept.Label),
                    CsvEscape(concept.ValuePattern ?? string.Empty),
                    CsvEscape(concept.TemplateScope ?? string.Empty),
                    CsvEscape(alias),
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty));
            }
        }

        foreach (var fact in glossary.Facts)
        {
            builder.AppendLine(string.Join(",",
                CsvEscape(fact.ConceptKey),
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                CsvEscape(fact.SourceName),
                fact.SourceId.ToString(),
                CsvEscape(fact.Value),
                fact.PageNumber?.ToString() ?? string.Empty));
        }

        return builder.ToString();
    }

    private static string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }
}

/// <summary>Request body for POST /api/lexicon/unmapped/resolve.</summary>
public sealed record ResolveUnmappedTermRequest(string Term, Guid SourceId);

/// <summary>End-user glossary payload: concepts + verified facts (with source names).</summary>
public sealed class GlossaryResponse
{
    public IReadOnlyList<LexiconConcept> Concepts { get; set; } = Array.Empty<LexiconConcept>();
    public IReadOnlyList<GlossaryFact> Facts { get; set; } = Array.Empty<GlossaryFact>();
}

/// <summary>A verified fact surfaced in the glossary, joined with its source name.</summary>
public sealed class GlossaryFact
{
    public Guid SourceId { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public string ConceptKey { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string SourceSpan { get; set; } = string.Empty;
    public int? PageNumber { get; set; }
    public int? OffsetInPage { get; set; }
}
