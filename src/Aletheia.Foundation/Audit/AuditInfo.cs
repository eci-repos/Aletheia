namespace Aletheia.Foundation.Audit;

public sealed class AuditInfo
{
    public AuditInfo(DateTimeOffset createdAt, AuditActor createdBy, DateTimeOffset? lastModifiedAt = null, AuditActor? lastModifiedBy = null)
    {
        CreatedAt = createdAt;
        CreatedBy = createdBy ?? throw new ArgumentNullException(nameof(createdBy));
        LastModifiedAt = lastModifiedAt;
        LastModifiedBy = lastModifiedBy;
    }

    public DateTimeOffset CreatedAt { get; }

    public AuditActor CreatedBy { get; }

    public DateTimeOffset? LastModifiedAt { get; }

    public AuditActor? LastModifiedBy { get; }
}
