namespace Aletheia.Repository.Abstractions.Models;

public sealed class FileDescriptor
{
    public FileDescriptor(Guid fileId, string fileName, string? version = null)
    {
        if (fileId == Guid.Empty)
        {
            throw new ArgumentException("File ID is required.", nameof(fileId));
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("File name is required.", nameof(fileName));
        }

        FileId = fileId;
        FileName = fileName;
        Version = string.IsNullOrWhiteSpace(version) ? null : version;
    }

    public Guid FileId { get; }

    public string FileName { get; }

    public string? Version { get; }
}
