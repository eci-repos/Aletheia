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
