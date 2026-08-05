using Aletheia.Web.Services;

namespace Aletheia.Web.UnitTests;

public class RecentGraphContextServiceTests
{
    [Fact]
    public void Upsert_deduplicates_existing_item_and_keeps_newest_first()
    {
        var sourceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var older = RecentGraphContextItem.Document(sourceId, "Old name", DateTimeOffset.UtcNow.AddMinutes(-10));
        var newer = RecentGraphContextItem.Document(sourceId, "New name", DateTimeOffset.UtcNow);

        var updated = RecentGraphContextService.Upsert(new[] { older }, newer);

        Assert.Single(updated);
        Assert.Equal("New name", updated[0].Label);
        Assert.Equal(newer.Timestamp, updated[0].Timestamp);
    }

    [Fact]
    public void Upsert_keeps_ten_recent_items_per_kind()
    {
        var current = Enumerable.Range(0, 14)
            .Select(index => RecentGraphContextItem.Search(
                $"query {index}",
                "semantic",
                DateTimeOffset.UtcNow.AddMinutes(-index)))
            .ToList();
        var newest = RecentGraphContextItem.Search("query newest", "semantic", DateTimeOffset.UtcNow.AddMinutes(1));

        var updated = RecentGraphContextService.Upsert(current, newest);

        Assert.Equal(10, updated.Count(item => item.Kind == "search"));
        Assert.Equal("query newest", updated[0].Label);
        Assert.DoesNotContain(updated, item => item.Label == "query 13");
    }

    [Fact]
    public void Deserialize_returns_empty_list_for_invalid_json()
    {
        var result = RecentGraphContextService.Deserialize("{not json");

        Assert.Empty(result);
    }
}
