using Aletheia.Foundation.Audit;
using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Models;

namespace Aletheia.Repository.Abstractions.Interfaces;

public interface IAuditService
{
    Task<Result<AuditInfo>> RecordAsync(
        FileDescriptor descriptor,
        AuditInfo auditInfo,
        CancellationToken cancellationToken = default);
}
