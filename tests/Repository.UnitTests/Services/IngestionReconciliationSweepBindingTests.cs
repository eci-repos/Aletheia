namespace Repository.UnitTests.Services;

/// <summary>
/// Sprint 73 post-fix: the reconciliation sweep's candidate predicate must be embeddings-only.
/// A source with a stale last_ingested_at marker but zero embeddings (an interrupted re-ingest that
/// deleted the old rows before the write-new-then-swap landed) must still be a repair candidate —
/// the marker is stamped on completion and is not a sweep gate. These binding tests read the
/// repository source so a regression that re-gates the sweep on the marker fails the build.
/// </summary>
public class IngestionReconciliationSweepBindingTests
{
    [Fact]
    public void Sweep_predicate_targets_zero_embeddings_without_marker_gate()
    {
        var source = File.ReadAllText(FindRepoFile("src/Repository.Infrastructure.PostgreSQL/Metadata/PostgreSqlMetadataRepository.cs"));

        // The sweep must select sources with no embeddings...
        Assert.Contains("NOT EXISTS", source);
        Assert.Contains("FROM embeddings e", source);
        Assert.Contains("e.source_id = fm.file_id", source);

        // ...and must NOT gate on the last_ingested_at marker (a stale marker hides a broken source).
        Assert.DoesNotContain("last_ingested_at IS NULL", source);
    }

    [Fact]
    public void Sweep_predicate_lives_in_GetSourcesMissingIngestionAsync()
    {
        var source = File.ReadAllText(FindRepoFile("src/Repository.Infrastructure.PostgreSQL/Metadata/PostgreSqlMetadataRepository.cs"));

        var methodStart = source.IndexOf("GetSourcesMissingIngestionAsync", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "GetSourcesMissingIngestionAsync must exist in the metadata repository.");

        var methodBody = source.Substring(methodStart);
        Assert.Contains("SELECT fm.file_id", methodBody);
        Assert.Contains("WHERE NOT EXISTS", methodBody);
    }

    private static string FindRepoFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not find {relativePath} from {AppContext.BaseDirectory}.");
    }
}
