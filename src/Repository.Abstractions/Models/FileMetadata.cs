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
        AuditInfo? auditInfo = null,
        string? contentHash = null)
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
        ContentHash = string.IsNullOrWhiteSpace(contentHash) ? null : contentHash;
    }

    public FileDescriptor Descriptor { get; }

    public string ContentType { get; }

    public long SizeBytes { get; }

    public DateTimeOffset UploadedAt { get; }

    public IReadOnlyDictionary<string, string> Tags { get; }

    public AuditInfo? AuditInfo { get; }

    /// <summary>SHA-256 (hex) of the uploaded content, when fingerprinting is enabled. Null for pre-existing rows.</summary>
    public string? ContentHash { get; }
}
