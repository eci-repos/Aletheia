namespace Aletheia.Foundation.Context;

public sealed class TenantContext
{
    public TenantContext(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        TenantId = tenantId;
    }

    public string TenantId { get; }
}
