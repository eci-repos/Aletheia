namespace Aletheia.Foundation.Context;

public sealed class CorrelationContext
{
    public CorrelationContext(Guid correlationId)
    {
        CorrelationId = correlationId == Guid.Empty ? Guid.NewGuid() : correlationId;
    }

    public Guid CorrelationId { get; }

    public static CorrelationContext New() => new(Guid.NewGuid());
}
