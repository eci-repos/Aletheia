using Aletheia.Foundation.Audit;

namespace Aletheia.Repository.Abstractions.Models;

public sealed class FileMetadata
{
    public FileMetadata(
        FileDescriptor descriptor,
        string contentType,
        long sizeBytes,
        DateTimeOffset uploadedAt,
        IReadOnlyDictionary<string, string>? tags = null,
        AuditInfo? auditInfo = null)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));

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
        UploadedAt = uploadedAt;
        Tags = tags is null ? new Dictionary<string, string>() : new Dictionary<string, string>(tags);
        AuditInfo = auditInfo;
    }

    public FileDescriptor Descriptor { get; }

    public string ContentType { get; }

    public long SizeBytes { get; }

    public DateTimeOffset UploadedAt { get; }

    public IReadOnlyDictionary<string, string> Tags { get; }

    public AuditInfo? AuditInfo { get; }
}
