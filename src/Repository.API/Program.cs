using Aletheia.KnowledgeGraph.Abstractions.Interfaces;
using Aletheia.KnowledgeGraph.Infrastructure.Neo4j.GraphStore;
using Aletheia.RAGS.Abstractions.Configuration;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Application;
using Aletheia.RAGS.Application.Configuration;
using Aletheia.RAGS.Application.GraphRAG;
using Aletheia.RAGS.Application.LazyGraphRAG;
using Aletheia.RAGS.Application.Pipelines;
using Aletheia.RAGS.Application.Providers;
using Aletheia.RAGS.Infrastructure.PgVector.VectorStore;
using Aletheia.RAGS.Infrastructure.PostgreSQL.Ontology;
using Aletheia.RAGS.Infrastructure.PostgreSQL.Taxonomy;
using Aletheia.RAGS.Infrastructure.PostgreSQL.Wiki;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.API.HealthChecks;
using Aletheia.Repository.API.Middleware;
using Aletheia.Repository.API.Services;
using Aletheia.Repository.Application;
using Aletheia.Repository.Application.UseCases;
using Aletheia.Repository.Application.UseCases.Collaboration;
using Aletheia.Repository.Application.UseCases.Governance;
using Aletheia.Repository.Domain.UseCases;
using Aletheia.Repository.Infrastructure.MinIO.Storage;
using Aletheia.Repository.Infrastructure.PostgreSQL.Connections;
using Aletheia.Repository.Infrastructure.PostgreSQL.Metadata;
using Aletheia.Repository.Infrastructure.PostgreSQL.Search;
using Aletheia.Repository.Infrastructure.PostgreSQL.Security;
using Aletheia.Repository.Infrastructure.PostgreSQL.Versioning;
using Aletheia.Security.Authentication;
using Aletheia.Security.Services;
using Aletheia.Security;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Minio;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// PostgreSQL
builder.Services.AddSingleton(_ =>
{
    var connectionString = builder.Configuration.GetConnectionString("PostgreSQL")
        ?? throw new InvalidOperationException("PostgreSQL connection string is required.");
    return new PostgreSqlConnectionFactory(connectionString);
});

builder.Services.AddSingleton<IUserStore, PostgreSqlUserStore>();
builder.Services.AddSingleton<IRefreshTokenStore, PostgreSqlRefreshTokenStore>();
// builder.Services.AddHostedService<PostgreSqlSecuritySchemaInitializer>(); // Disabled for integration tests (no DB)
builder.Services.AddAletheiaSecurity(builder.Configuration);

builder.Services.AddSingleton<IMetadataRepository, PostgreSqlMetadataRepository>();
builder.Services.AddSingleton<Aletheia.Repository.Application.IDuplicateDetectionService, Aletheia.Repository.Application.DuplicateDetectionService>();
builder.Services.AddSingleton<IVersioningService, PostgreSqlVersioningService>();
builder.Services.AddSingleton<ISearchProvider, PostgreSqlSearchProvider>();

// MinIO
builder.Services.AddSingleton<IMinioClient>(_ =>
{
    var endpoint = builder.Configuration["MinIO:Endpoint"]
        ?? throw new InvalidOperationException("MinIO endpoint is required.");
    var accessKey = builder.Configuration["MinIO:AccessKey"]
        ?? throw new InvalidOperationException("MinIO access key is required.");
    var secretKey = builder.Configuration["MinIO:SecretKey"]
        ?? throw new InvalidOperationException("MinIO secret key is required.");

    return new MinioClient()
        .WithEndpoint(endpoint)
        .WithCredentials(accessKey, secretKey)
        .WithSSL(false)
        .Build();
});

builder.Services.AddSingleton<IStorageProvider>(sp =>
{
    var client = sp.GetRequiredService<IMinioClient>();
    var bucketName = builder.Configuration["MinIO:BucketName"] ?? "aletheia-files";
    return new MinioStorageProvider(client, bucketName);
});

// Use cases
builder.Services.AddSingleton<IUploadUseCase, UploadUseCase>();
builder.Services.AddSingleton<IDownloadUseCase, DownloadUseCase>();
builder.Services.AddSingleton<IDeleteUseCase, DeleteUseCase>();
builder.Services.AddSingleton<ISearchUseCase, SearchUseCase>();
builder.Services.AddSingleton<IMetadataUseCase, MetadataUseCase>();
builder.Services.AddSingleton<IVersioningUseCase, VersioningUseCase>();
builder.Services.AddSingleton<IRepositoryService, RepositoryService>();
builder.Services.AddSingleton<IUploadedFileTextExtractor, UploadedFileTextExtractor>();
builder.Services.AddSingleton<IUploadedContentKnowledgeIndexer, UploadedContentKnowledgeIndexer>();
builder.Services.AddSingleton<IKnowledgeSourceIngestionService, RepositoryKnowledgeSourceIngestionService>();
builder.Services.AddSingleton<IngestionJobService>();
builder.Services.AddSingleton<Aletheia.Repository.API.Services.IIngestionDiagnostics, Aletheia.Repository.API.Services.IngestionDiagnostics>();
builder.Services.AddSingleton<Aletheia.Repository.API.Services.IRagsStatusService, Aletheia.Repository.API.Services.RagsStatusService>();
builder.Services.AddSingleton<IIngestionJobService>(sp => sp.GetRequiredService<IngestionJobService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<IngestionJobService>());
builder.Services.AddSingleton(sp => new Lazy<Aletheia.Repository.API.Services.IIngestionJobService>(sp.GetRequiredService<Aletheia.Repository.API.Services.IIngestionJobService>));

// AI / Semantic Kernel
builder.Services.AddAletheiaAI(builder.Configuration);

// RAGS
builder.Services.Configure<Aletheia.RAGS.Abstractions.Configuration.PgVectorOptions>(builder.Configuration.GetSection(Aletheia.RAGS.Abstractions.Configuration.PgVectorOptions.SectionName));
builder.Services.Configure<Aletheia.RAGS.Abstractions.Configuration.RetrievalOptions>(builder.Configuration.GetSection(Aletheia.RAGS.Abstractions.Configuration.RetrievalOptions.SectionName));
    builder.Services.Configure<Aletheia.RAGS.Abstractions.Configuration.TaxonomyOptions>(builder.Configuration.GetSection(Aletheia.RAGS.Abstractions.Configuration.TaxonomyOptions.SectionName));
    builder.Services.AddSingleton<ITermNormalizer, ConfigurableTermNormalizer>();
builder.Services.AddSingleton<ChunkingPipeline>();
builder.Services.AddSingleton<IVectorStore>(sp =>
{
    var connectionFactory = sp.GetRequiredService<PostgreSqlConnectionFactory>();
    var embeddingProvider = sp.GetRequiredService<IEmbeddingProvider>();
    var pgVectorOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Aletheia.RAGS.Abstractions.Configuration.PgVectorOptions>>().Value;
    return new PgVectorStore(connectionFactory, embeddingProvider.VectorDimension, pgVectorOptions.CommandTimeoutSeconds);
});
builder.Services.AddSingleton<IRagsService, RagsService>();
builder.Services.AddSingleton<Aletheia.RAGS.Abstractions.Interfaces.IKnowledgeThemeService, Aletheia.RAGS.Application.KnowledgeThemeService>();
builder.Services.AddSingleton<Aletheia.Repository.API.Services.TemplateReevaluationService>();
builder.Services.AddHostedService(sp =>
{
    var connectionFactory = sp.GetRequiredService<PostgreSqlConnectionFactory>();
    var embeddingProvider = sp.GetRequiredService<IEmbeddingProvider>();
    var pgVectorOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Aletheia.RAGS.Abstractions.Configuration.PgVectorOptions>>().Value;
    var logger = sp.GetRequiredService<ILogger<Aletheia.RAGS.Infrastructure.PgVector.Schema.PgVectorSchemaInitializer>>();
    return new Aletheia.RAGS.Infrastructure.PgVector.Schema.PgVectorSchemaInitializer(
        connectionFactory,
        embeddingProvider.VectorDimension,
        pgVectorOptions.VectorIndexType,
        logger);
});
builder.Services.AddSingleton<ITaxonomyProvider, TaxonomyService>();
builder.Services.AddSingleton<IOntologyProvider, OntologyService>();
builder.Services.AddSingleton<ILazyEnrichmentKnowledgeSink, Aletheia.RAGS.Infrastructure.PostgreSQL.Knowledge.LazyEnrichmentKnowledgeSink>();
builder.Services.AddSingleton<PostgreSqlWikiSchema>();
// builder.Services.AddHostedService<PostgreSqlWikiSchemaInitializer>();
builder.Services.AddSingleton<IWikiPageRepository, PostgreSqlWikiPageRepository>();
builder.Services.Configure<Aletheia.RAGS.Abstractions.Configuration.FeatureFlagsOptions>(builder.Configuration.GetSection(Aletheia.RAGS.Abstractions.Configuration.FeatureFlagsOptions.SectionName));
builder.Services.AddSingleton<Aletheia.RAGS.Abstractions.Interfaces.IInternalSearchGate, Aletheia.RAGS.Application.InternalSearchGate>();
builder.Services.AddSingleton<Aletheia.RAGS.Abstractions.Interfaces.IDocumentBriefGenerator, Aletheia.RAGS.Application.DocumentBriefs.SemanticKernelDocumentBriefGenerator>();
builder.Services.AddSingleton<Aletheia.RAGS.Abstractions.Interfaces.IDocumentBriefService, Aletheia.RAGS.Application.DocumentBriefs.DocumentBriefService>();

// GraphRAG + LazyGraphRAG services (registered below with intelligence wiring)
// Collaboration
builder.Services.AddSingleton<ICollaborationService, CollaborationService>();

// Governance
builder.Services.AddSingleton<IGovernanceService, GovernanceService>();

// Graph SDK / Provider
builder.Services.AddSingleton<IGraphProvider>(_ =>
{
    var neo4jUri = builder.Configuration["Neo4j:Uri"] ?? "bolt://localhost:7687";
    var neo4jUser = builder.Configuration["Neo4j:Username"] ?? "neo4j";
    var neo4jPassword = builder.Configuration["Neo4j:Password"] ?? "aletheia";
    return new Aletheia.RAGS.Infrastructure.Graph.Providers.Neo4jGraphProvider(neo4jUri, neo4jUser, neo4jPassword);
});

// Graph SDK Services
builder.Services.AddSingleton<Aletheia.KnowledgeGraph.Abstractions.Interfaces.IGraphService, Aletheia.RAGS.Application.Graph.GraphService>();
builder.Services.AddSingleton<IGraphQueryService, Aletheia.RAGS.Application.Graph.GraphQueryService>();
builder.Services.AddSingleton<IGraphAdminService, Aletheia.RAGS.Application.Graph.GraphAdminService>();
builder.Services.AddSingleton<IGraphImportExportService, Aletheia.RAGS.Application.Graph.GraphImportExportService>();
builder.Services.AddSingleton<IGraphAnalyticsService, Aletheia.RAGS.Application.Graph.GraphAnalyticsService>();

// GraphRAG Intelligence Services
builder.Services.AddSingleton<IEntityExtractionService, Aletheia.RAGS.Application.GraphIntelligence.EntityExtractionService>();
builder.Services.AddSingleton<IEntityResolutionService, Aletheia.RAGS.Application.GraphIntelligence.EntityResolutionService>();
builder.Services.AddSingleton<IRelationshipExtractionService, Aletheia.RAGS.Application.GraphIntelligence.RelationshipExtractionService>();
builder.Services.AddSingleton<ICommunityDetectionService, Aletheia.RAGS.Application.GraphIntelligence.CommunityDetectionService>();
builder.Services.AddSingleton<IGraphSummaryService, Aletheia.RAGS.Application.GraphIntelligence.GraphSummaryService>();
builder.Services.AddSingleton<IHierarchicalSummaryService, Aletheia.RAGS.Application.GraphIntelligence.HierarchicalSummaryService>();
builder.Services.AddSingleton<IGraphReasoningService, Aletheia.RAGS.Application.GraphIntelligence.GraphReasoningService>();
builder.Services.AddSingleton<IGraphContextBuilder, Aletheia.RAGS.Application.GraphIntelligence.GraphContextBuilder>();
builder.Services.AddSingleton<ICitationPathService, Aletheia.RAGS.Application.GraphIntelligence.CitationPathService>();
builder.Services.AddSingleton<IGlobalGraphSearchService, Aletheia.RAGS.Application.GraphRAG.GlobalGraphSearchService>();
builder.Services.AddSingleton<IWragsWikiService, Aletheia.RAGS.Application.Wiki.WragsWikiService>();

// GraphRAG + LazyGraphRAG services (with intelligence wiring)
builder.Services.AddSingleton<IGraphRagService, Aletheia.RAGS.Application.GraphRAG.GraphRagService>();
builder.Services.AddSingleton<ILazyGraphRagService, Aletheia.RAGS.Application.LazyGraphRAG.LazyGraphRagService>();

// LazyGraphRAG discovery services (Sprint-15)
builder.Services.AddSingleton<ICorpusDiscoveryIndex, Aletheia.RAGS.Application.LazyGraphRAG.CorpusDiscoveryIndex>();
builder.Services.AddSingleton<ILazyEntityDiscoveryService, Aletheia.RAGS.Application.LazyGraphRAG.LazyEntityDiscoveryService>();
builder.Services.AddSingleton<ILazyRelationshipDiscoveryService, Aletheia.RAGS.Application.LazyGraphRAG.LazyRelationshipDiscoveryService>();
// NOTE: IGraphTraversalBudget is intentionally NOT registered as a singleton. Since Sprint 60,
// each retrieval request constructs its own budget (LazyGraphRagService/GraphRagService create a
// fresh GraphTraversalBudget per RetrieveAsync call) so concurrent requests cannot corrupt each other.
builder.Services.AddSingleton<ISubgraphPruningService, Aletheia.RAGS.Application.LazyGraphRAG.SubgraphPruningService>();

// Legacy Knowledge Graph bridge
builder.Services.AddSingleton<GraphSyncService>();

builder.Services.AddHealthChecks()
    .AddCheck<PostgreSqlHealthCheck>("postgresql", tags: new[] { "db", "ready" })
    .AddCheck<Neo4jHealthCheck>("neo4j", tags: new[] { "db", "ready" })
    .AddCheck<MinioHealthCheck>("minio", tags: new[] { "storage", "ready" });

// CORS: allow origins from configuration; fallback to localhost for development
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? new[] { "https://localhost:5001", "http://localhost:5000" };
builder.Services.AddCors(options =>
{
    options.AddPolicy("ProductionCors", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Global exception handling first
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

// Security headers before all other middleware
app.UseMiddleware<SecurityHeadersMiddleware>();

// Correlation IDs for distributed tracing
app.UseMiddleware<CorrelationIdMiddleware>();

// Audit logging for mutating operations
app.UseMiddleware<AuditLogMiddleware>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
}

app.UseCors("ProductionCors");
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

app.Run();

public partial class Program { }






