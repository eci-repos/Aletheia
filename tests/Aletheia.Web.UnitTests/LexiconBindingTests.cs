using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.Web.UnitTests;

public class LexiconBindingTests
{
    [Fact]
    public void Lexicon_tables_exist_in_migration_and_init()
    {
        var migration = File.ReadAllText(FindRepoFile("src/Repository.Infrastructure.PostgreSQL/Migrations/2026-08-14-lexicon-and-facts.sql"));
        var init = File.ReadAllText(FindRepoFile("scripts/init.sql"));

        Assert.Contains("CREATE TABLE IF NOT EXISTS lexicon_concepts", migration);
        Assert.Contains("CREATE TABLE IF NOT EXISTS lexicon_aliases", migration);
        Assert.Contains("CREATE TABLE IF NOT EXISTS document_facts", migration);
        Assert.Contains("CREATE TABLE IF NOT EXISTS lexicon_unmapped_terms", migration);
        Assert.Contains("CREATE TABLE IF NOT EXISTS lexicon_concepts", init);
        Assert.Contains("CREATE TABLE IF NOT EXISTS document_facts", init);
    }

    [Fact]
    public void Migration_seed_mirrors_lexicon_seed_data()
    {
        var migration = File.ReadAllText(FindRepoFile("src/Repository.Infrastructure.PostgreSQL/Migrations/2026-08-14-lexicon-and-facts.sql"));

        foreach (var concept in LexiconSeedData.Defaults)
        {
            Assert.Contains($"'{concept.Key}'", migration);
        }
    }

    [Fact]
    public void Init_seed_mirrors_lexicon_seed_data()
    {
        var init = File.ReadAllText(FindRepoFile("scripts/init.sql"));

        foreach (var concept in LexiconSeedData.Defaults)
        {
            Assert.Contains($"'{concept.Key}'", init);
        }
    }

    [Fact]
    public void Unmapped_status_columns_exist_in_schema_migration_and_init()
    {
        var migration = File.ReadAllText(FindRepoFile("src/Repository.Infrastructure.PostgreSQL/Migrations/2026-08-14-lexicon-unmapped-status.sql"));
        var init = File.ReadAllText(FindRepoFile("scripts/init.sql"));
        var schema = File.ReadAllText(FindRepoFile("src/RAGS.Infrastructure.PostgreSQL/Lexicon/PostgreSqlLexiconSchema.cs"));

        Assert.Contains("ADD COLUMN IF NOT EXISTS status text NOT NULL DEFAULT 'pending'", migration);
        Assert.Contains("ADD COLUMN IF NOT EXISTS resolved_at timestamptz NULL", migration);
        Assert.Contains("status text NOT NULL DEFAULT 'pending'", init);
        Assert.Contains("resolved_at timestamptz NULL", init);
        Assert.Contains("status text NOT NULL DEFAULT 'pending'", schema);
        Assert.Contains("resolved_at timestamptz NULL", schema);
    }

    [Fact]
    public void Glossary_page_exists_with_route()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Glossary/Index.razor"));

        Assert.Contains("@page \"/glossary\"", source);
        Assert.Contains("Download CSV", source);
        Assert.Contains("Download JSON", source);
    }

    [Fact]
    public void Nav_menu_has_glossary_entry()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Layout/NavMenu.razor"));

        Assert.Contains("href=\"glossary\"", source);
        Assert.Contains("icon-glossary", source);
    }

    [Fact]
    public void Lexicon_admin_page_exists_with_route_and_admin_gate()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Lexicon/Index.razor"));

        Assert.Contains("@page \"/lexicon\"", source);
        Assert.Contains("AuthorizeView Roles=\"Administrator\"", source);
        Assert.Contains("Unmapped terms", source);
    }

    [Fact]
    public void Nav_menu_has_admin_lexicon_entry()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Layout/NavMenu.razor"));

        Assert.Contains("href=\"lexicon\"", source);
        Assert.Contains("icon-lexicon", source);
    }

    [Fact]
    public void RepositoryApiClient_exposes_lexicon_surface()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Services/RepositoryApiClient.cs"));

        Assert.Contains("GetGlossaryAsync", source);
        Assert.Contains("ExportGlossaryAsync", source);
        Assert.Contains("GetLexiconConceptsAsync", source);
        Assert.Contains("UpsertLexiconConceptAsync", source);
        Assert.Contains("DeleteLexiconConceptAsync", source);
        Assert.Contains("GetUnmappedTermsAsync", source);
        Assert.Contains("ResolveUnmappedTermAsync", source);
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
