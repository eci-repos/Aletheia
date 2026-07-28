namespace Aletheia.Repository.Abstractions.Models;

public sealed class DeleteRequest
{
    public DeleteRequest(FileDescriptor descriptor)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
    }

    public FileDescriptor Descriptor { get; }
}
