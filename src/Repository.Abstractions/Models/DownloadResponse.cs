namespace Aletheia.Repository.Abstractions.Models;

public sealed class DownloadResponse
{
    public DownloadResponse(FileMetadata metadata, Stream content)
    {
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        Content = content ?? throw new ArgumentNullException(nameof(content));

        if (!content.CanRead)
        {
            throw new ArgumentException("Content stream must be readable.", nameof(content));
        }
    }

    public FileMetadata Metadata { get; }

    public Stream Content { get; }
}
