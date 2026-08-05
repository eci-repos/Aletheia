# commit-sprint55.ps1
# Stages the Sprint 55 (Document Briefs / End-User Wiki) file set and optionally commits it.
#
# Run in a PowerShell with write access to .git (the Codex sandbox is read-only for .git):
#   ./commit-sprint55.ps1            # stage only, print review summary
#   ./commit-sprint55.ps1 -Commit    # stage and commit
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# 1) Files that are 100% Sprint 55 (new files + files whose only changes are
#    this sprint's) - stage in full.
# ---------------------------------------------------------------------------
$fullAdd = @(
  # New abstractions
  'src/RAGS.Abstractions/Configuration/FeatureFlagsOptions.cs'
  'src/RAGS.Abstractions/Interfaces/IInternalSearchGate.cs'
  'src/RAGS.Abstractions/Interfaces/IDocumentBriefService.cs'
  'src/RAGS.Abstractions/Interfaces/IDocumentBriefGenerator.cs'
  'src/RAGS.Abstractions/Models/DocumentBriefRequest.cs'
  'src/RAGS.Abstractions/Models/DocumentBriefRegenerationResult.cs'
  'src/RAGS.Abstractions/Models/DocumentBriefRegenerationRequest.cs'
  # New application code
  'src/RAGS.Application/InternalSearchGate.cs'
  'src/RAGS.Application/DocumentBriefs/DocumentBriefService.cs'
  'src/RAGS.Application/DocumentBriefs/SemanticKernelDocumentBriefGenerator.cs'
  # Controllers / infra / app config (clean before this sprint)
  'src/Repository.API/Controllers/WikiController.cs'
  'src/Repository.API/Controllers/GraphRagController.cs'
  'src/Repository.API/Controllers/LazyGraphRagController.cs'
  'src/Repository.API/Controllers/Graph/GraphQueryController.cs'
  'src/RAGS.Infrastructure.PostgreSQL/Wiki/PostgreSqlWikiPageRepository.cs'
  'src/Aletheia.Web/wwwroot/appsettings.json'
  'src/Aletheia.Web/Layout/NavMenu.razor'
  'src/Aletheia.Web/Pages/Wiki.razor'
  'src/Aletheia.Web/Pages/SearchCenter.razor'
  'src/Aletheia.Web/Pages/Dashboard.razor'
  'src/Aletheia.Web/Pages/MetadataEditor.razor'
  'src/Aletheia.Web/Services/RecentGraphContextService.cs'
  # Tests (new)
  'tests/RAGS.UnitTests/TestSupport/FakeInternalSearchGate.cs'
  'tests/RAGS.UnitTests/DocumentBriefs/DocumentBriefServiceTests.cs'
  'tests/RAGS.UnitTests/FeatureFlags/InternalSearchGateTests.cs'
  'tests/RAGS.UnitTests/Wiki/WikiControllerInternalSearchGateTests.cs'
  'tests/RAGS.UnitTests/GraphRAG/GraphRagControllerTests.cs'
  'tests/RAGS.UnitTests/LazyGraphRAG/LazyGraphRagControllerTests.cs'
  # Docs / handoff
  'AGENTS.md'
  'README.md'
  'docs/Architecture.md'
  'docs/AdministratorGuide.md'
  'docs/OperationsGuide.md'
  'docs/File 02-Current-Sprint.md'
  'docs/sprints/Sprint-55 - Document Briefs and End-User Wiki.md'
)

# Files that were already modified before this sprint (prior uncommitted work)
# and that this sprint also touched. Their working-tree content includes
# non-Sprint-55 hunks, so they cannot be staged hunk-purely; stage in full.
$sharedAdd = @(
  'src/Repository.API/Program.cs'
  'src/Repository.API/appsettings.json'
  'src/Repository.API/Services/IngestionJobService.cs'
  'src/Repository.API/Services/RepositoryKnowledgeSourceIngestionService.cs'
  'src/RAGS.Application/SemanticKernel/RetrievalAugmentedPromptBuilder.cs'
  'src/Aletheia.Web/Services/RepositoryApiClient.cs'
  'src/Aletheia.Web/wwwroot/css/app.css'
  'src/Aletheia.Web/Pages/Upload.razor'
  'src/Aletheia.Web/Pages/GraphExplorer.razor'
  'tests/RAGS.UnitTests/BackgroundJobs/JobsControllerTests.cs'
  'docs/File 03-openhands.md'
)

Write-Host '== Staging Sprint 55 files =='
git add -- $fullAdd $sharedAdd
if ($LASTEXITCODE -ne 0) { throw 'git add failed' }

# ---------------------------------------------------------------------------
# 2) Hunk refinement: RepositoryApiClient.cs has exactly one Sprint-55 hunk
#    (RegenerateDocumentBriefsAsync) at the top of the diff, followed by four
#    pre-existing hunks. Re-stage it hunk-selectively with y/n/n/n/n.
# ---------------------------------------------------------------------------
git restore --staged -- 'src/Aletheia.Web/Services/RepositoryApiClient.cs'
$responses = @('y', 'n', 'n', 'n', 'n', 'q')
$responses | git add -p -- 'src/Aletheia.Web/Services/RepositoryApiClient.cs' | Out-Null
if ($LASTEXITCODE -ne 0) {
  Write-Warning 'git add -p refinement failed for RepositoryApiClient.cs; re-staging the file in full.'
  git add -- 'src/Aletheia.Web/Services/RepositoryApiClient.cs'
}

# ---------------------------------------------------------------------------
# 3) Review summary
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '== Staged changes (review before committing) =='
git status --short
Write-Host ''
git diff --cached --stat

Write-Host ''
Write-Host '== Non-Sprint-55 hunks that ride along in shared files =='
Write-Host @"
- Program.cs ................. PgVector/Taxonomy wiring, PgVectorStore ctor arg,
                              PgVectorSchemaInitializer hosted service, schema-init comments
- appsettings.json ........... Logging/Auth/ConnStr/AI/MinIO/ChatAgent/ChatExecutionEngine/PgVector/Taxonomy settings
- IngestionJobService.cs ..... prior RagsRepair/wiki-regeneration/knowledge-indexer work
- RepositoryKnowledgeSourceIngestionService.cs ... Sprint 53/54 template gate + logging
- RetrievalAugmentedPromptBuilder.cs ............... Sprint 53 sectionOutline scaffold (file rewritten by this sprint)
- Wiki.razor ................. prior WRAGS lifecycle/history/related UI (file rewritten by this sprint)
- SearchCenter.razor ......... prior modes/ingest UI (file rewritten by this sprint)
- RepositoryApiClient.cs ..... only the RegenerateDocumentBriefsAsync hunk is staged (others excluded)
- JobsControllerTests.cs ..... prior RepairRags test + EnqueueRagsRepair fake (mixed hunk)
- AGENTS.md .................. prior authority/canonical-template bullets
- docs/Architecture.md ....... prior Search Center/WRAGS text, Sprint 50/53/54 sections
- docs/AdministratorGuide.md . prior RAGS admin content
"@

# ---------------------------------------------------------------------------
# 4) Commit (only with -Commit)
# ---------------------------------------------------------------------------
if ($Commit) {
  $message = @"
Sprint 55: document briefs and the end-user Wiki

- DocumentBriefService + SemanticKernelDocumentBriefGenerator generate
  per-document briefs (nature/purpose first, canonical template sections
  in order, grounded and cited) stored as wiki_pages rows with
  generated_from = 'document-brief', primary_source_id = the document.
- IngestionJobService DocumentBriefs background job + POST
  /api/wiki/briefs/regenerate; triggers after EnsureIngestedAsync and
  after upload ingestion jobs.
- Wiki search/recent exclude community summaries (generated_from =
  'graphrag') and order document briefs first.
- FeatureFlags:ShowInternalSearch (default false) gates GraphRAG,
  LazyGraphRAG, global-graph, and internal wiki modes (HTTP 404); the
  Search Center and Wiki UI hide the internal controls and user-facing
  labels are renamed to Wiki.
- Tests: DocumentBriefService, InternalSearchGate, WikiController gating,
  GraphRAG/LazyGraphRAG gating; RAGS/Foundation/Repository suites green.
- Docs: Architecture, AdministratorGuide, OperationsGuide, AGENTS, and
  sprint/handoff notes updated.
"@
  git commit -m $message
  if ($LASTEXITCODE -ne 0) { throw 'git commit failed' }
  Write-Host ''
  git log --oneline -1
} else {
  Write-Host ''
  Write-Host 'Staged only. Review with:  git diff --cached'
  Write-Host 'Then commit with:          ./commit-sprint55.ps1 -Commit'
}
