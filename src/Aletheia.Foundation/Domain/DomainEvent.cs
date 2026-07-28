namespace Aletheia.Foundation.Domain;

public abstract record DomainEvent
{
    protected DomainEvent()
    {
        EventId = Guid.NewGuid();
        OccurredOn = DateTimeOffset.UtcNow;
    }

    public Guid EventId { get; init; }

    public DateTimeOffset OccurredOn { get; init; }
}
