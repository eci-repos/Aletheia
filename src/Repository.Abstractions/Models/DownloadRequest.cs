namespace Aletheia.Repository.Abstractions.Models;

public sealed class DownloadRequest
{
    public DownloadRequest(FileDescriptor descriptor)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
    }

    public FileDescriptor Descriptor { get; }
}
