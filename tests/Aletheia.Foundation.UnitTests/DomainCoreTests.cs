using Aletheia.Foundation.Domain;

namespace Aletheia.Foundation.UnitTests;

public class DomainCoreTests
{
    [Fact]
    public void Entity_WithSameIdAndType_IsEqual()
    {
        var id = Guid.NewGuid();
        var left = new TestEntity(id);
        var right = new TestEntity(id);

        Assert.Equal(left, right);
        Assert.True(left == right);
        Assert.False(left != right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void Entity_WithSameIdDifferentType_IsNotEqual()
    {
        var id = Guid.NewGuid();
        var left = new TestEntity(id);
        var right = new OtherEntity(id);

        Assert.False(left.Equals(right));
        Assert.False(left == right);
    }

    [Fact]
    public void AggregateRoot_AddsAndClearsDomainEvents()
    {
        var aggregate = new TestAggregate();
        var domainEvent = new TestEvent("created");

        aggregate.Raise(domainEvent);

        Assert.Single(aggregate.DomainEvents);

        aggregate.ClearDomainEvents();

        Assert.Empty(aggregate.DomainEvents);
    }

    [Fact]
    public void ValueObject_WithSameComponents_IsEqual()
    {
        var left = new TestValueObject("alpha", 5);
        var right = new TestValueObject("alpha", 5);

        Assert.Equal(left, right);
        Assert.True(left == right);
        Assert.False(left != right);
    }

    [Fact]
    public void DomainEvent_InitializesDefaults()
    {
        var domainEvent = new TestEvent("ready");

        Assert.NotEqual(Guid.Empty, domainEvent.EventId);
        Assert.NotEqual(default, domainEvent.OccurredOn);
    }

    private sealed class TestEntity : Entity
    {
        public TestEntity(Guid id)
            : base(id)
        {
        }
    }

    private sealed class OtherEntity : Entity
    {
        public OtherEntity(Guid id)
            : base(id)
        {
        }
    }

    private sealed class TestAggregate : AggregateRoot
    {
        public void Raise(DomainEvent domainEvent) => AddDomainEvent(domainEvent);
    }

    private sealed class TestValueObject : ValueObject
    {
        public TestValueObject(string name, int count)
        {
            Name = name;
            Count = count;
        }

        public string Name { get; }

        public int Count { get; }

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Name;
            yield return Count;
        }
    }

    private sealed record TestEvent(string Name) : DomainEvent;
}
