namespace Aletheia.Repository.Abstractions.Models;

public sealed class UploadResponse
{
    public UploadResponse(FileMetadata metadata)
    {
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    public FileMetadata Metadata { get; }
}
