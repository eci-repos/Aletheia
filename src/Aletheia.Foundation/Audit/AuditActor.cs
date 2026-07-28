namespace Aletheia.Foundation.Audit;

public sealed class AuditActor
{
    public AuditActor(string actorId, string actorType, string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            throw new ArgumentException("Actor id is required.", nameof(actorId));
        }

        if (string.IsNullOrWhiteSpace(actorType))
        {
            throw new ArgumentException("Actor type is required.", nameof(actorType));
        }

        ActorId = actorId;
        ActorType = actorType;
        DisplayName = displayName;
    }

    public string ActorId { get; }

    public string ActorType { get; }

    public string? DisplayName { get; }
}
