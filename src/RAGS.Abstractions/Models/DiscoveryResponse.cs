namespace Aletheia.RAGS.Abstractions.Models;

public class DiscoveryResponse
{
    public IReadOnlyList<DiscoveryTopic> Topics { get; set; } = Array.Empty<DiscoveryTopic>();
}

public class DiscoveryTopic
{
    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public IReadOnlyList<SearchResult> Sources { get; set; } = Array.Empty<SearchResult>();
}
