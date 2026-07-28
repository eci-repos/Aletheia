namespace Aletheia.Repository.Abstractions.Models;

public sealed class UploadRequest
{
    public UploadRequest(
        FileDescriptor descriptor,
        Stream content,
        string contentType,
        long sizeBytes,
        IReadOnlyDictionary<string, string>? tags = null)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        Content = content ?? throw new ArgumentNullException(nameof(content));

        if (!content.CanRead)
        {
            throw new ArgumentException("Content stream must be readable.", nameof(content));
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new ArgumentException("Content type is required.", nameof(contentType));
        }

        if (sizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), "Size must be zero or greater.");
        }

        ContentType = contentType;
        SizeBytes = sizeBytes;
        Tags = tags is null ? new Dictionary<string, string>() : new Dictionary<string, string>(tags);
    }

    public FileDescriptor Descriptor { get; }

    public Stream Content { get; }

    public string ContentType { get; }

    public long SizeBytes { get; }

    public IReadOnlyDictionary<string, string> Tags { get; }
}
