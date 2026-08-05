### Sprint 42 - External RAGS Orchestration and Index Repair
**Status:** Completed.

#### Objective
Make Copilot's RAGS behavior externally steerable and add a background repair path for cases where Repository metadata or Taxonomy identifies relevant documents but Semantic/Vector RAG chunks are missing.

---

#### Background
The observed RFP trace resolved two registered RFP documents through metadata, and `RFP` exists in Taxonomy, but source-scoped RAGS retrieval returned no internal context. This points to index drift or missing vector chunks, not lack of repository knowledge.

---

#### Goals
* **External Orchestration Playbook:** Load Copilot repository-flow instructions from an external file usable by Semantic Kernel prompts.
* **Consistent Prompt Use:** Include the playbook in both direct SK chat and RAG-augmented Copilot synthesis.
* **RAGS Repair Job:** Queue a background job that scans registered Repository metadata and rehydrates matching documents into RAGS.
* **Scoped Repair:** Allow operators to repair a subset such as `RFP` without rebuilding every registered artifact.
* **Traceability:** Keep the repair job visible through the existing Activity/background job model.

---

#### Implementation Notes
* Added `ChatAgentOptions.OrchestrationScriptPath`.
* Added `IChatAgentInstructionProvider` and `FileChatAgentInstructionProvider`.
* Added `src/Repository.API/Prompts/copilot-rags-orchestration.md` and copied it to API publish output.
* Registered the instruction provider in `AddAletheiaAI(...)`.
* Added `IngestionJobEngine.RagsRepair`, `IIngestionJobService.EnqueueRagsRepair(...)`, and `POST /api/jobs/rags/repair?query=...`.
* Added `RepositoryApiClient.RepairRagsIndexAsync(...)`.

---

#### Exit Criteria
* `AletheiaKnowledgePlugin.SearchRags` guidance is externally editable through the playbook.
* RFP metadata/taxonomy matches are explicitly handled as scope signals.
* Missing chunks are treated as index drift requiring repair.
* A repair job can run independently of the UI session.
* Repair progress reports document counts through the existing job snapshot.
* Focused RAGS tests and solution build pass.

---

#### Validation
* Added controller regression for `POST /api/jobs/rags/repair?query=RFP`.
* Added prompt-builder regression that verifies external orchestration instructions are included in RAG synthesis prompts.
* `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj --filter "FullyQualifiedName~JobsControllerTests|FullyQualifiedName~SemanticKernelCopilotServiceTests" --no-restore` passed with 17 tests.
* `dotnet test tests/Aletheia.Web.UnitTests/Aletheia.Web.UnitTests.csproj --no-restore` passed with 32 tests.
* `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj --no-restore` passed with 183 tests.
* `dotnet build Aletheia.slnx --no-restore` passed with the existing AngleSharp NU1902 warning.
* `docker compose up -d --build api web` rebuilt/restarted the API and Web containers.
* `GET /health/live`, `GET /health/ready`, and `GET http://localhost:8081/copilot` returned `200`.
* Verified `/app/Prompts/copilot-rags-orchestration.md` exists inside the API container.

---

#### Handoff Notes
If a future prompt resolves documents through Taxonomy/metadata but RAGS returns no chunks, do not keep increasing chat timeout. Queue `POST /api/jobs/rags/repair?query=<scope>` and watch Activity. For the reported RFP case, use `query=RFP`.

The next agent should prefer editing `src/Repository.API/Prompts/copilot-rags-orchestration.md` for orchestration tuning before embedding more instructions in C#.
