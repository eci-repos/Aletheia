using Aletheia.Foundation.Audit;
using Aletheia.Foundation.Context;
using Aletheia.Foundation.Domain;
using Aletheia.Foundation.Exceptions;
using Aletheia.Foundation.Shared;
using Aletheia.Foundation.Validation;

namespace Aletheia.Foundation.UnitTests;

public class ValidationResultEdgeTests
{
    [Fact]
    public void Constructor_throws_when_errors_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => new ValidationResult(null!));
    }

    [Fact]
    public void Constructor_filters_blank_errors()
    {
        var result = new ValidationResult(new[] { " ", "Missing field" });

        Assert.Single(result.Errors);
        Assert.Equal("Missing field", result.Errors.First());
    }

    [Fact]
    public void AddError_throws_when_error_is_blank()
    {
        var result = new ValidationResult();

        Assert.Throws<ArgumentException>(() => result.AddError(" "));
    }
}

public class ContextEdgeTests
{
    [Fact]
    public void CorrelationContext_generates_new_identifier_for_empty()
    {
        var context = new CorrelationContext(Guid.Empty);

        Assert.NotEqual(Guid.Empty, context.CorrelationId);
    }

    [Fact]
    public void SecurityContext_requires_user_id()
    {
        Assert.Throws<ArgumentException>(() => new SecurityContext(" ", new[] { "Admin" }));
    }

    [Fact]
    public void SecurityContext_filters_duplicate_roles()
    {
        var context = new SecurityContext("user-1", new[] { "Admin", " ", "Admin", "Reader" });

        Assert.Equal(2, context.Roles.Count);
        Assert.Contains("Admin", context.Roles);
        Assert.Contains("Reader", context.Roles);
    }

    [Fact]
    public void TenantContext_requires_tenant_id()
    {
        Assert.Throws<ArgumentException>(() => new TenantContext(" "));
    }
}

public class AuditEdgeTests
{
    [Fact]
    public void AuditActor_requires_actor_id()
    {
        Assert.Throws<ArgumentException>(() => new AuditActor(" ", "user"));
    }

    [Fact]
    public void AuditActor_requires_actor_type()
    {
        Assert.Throws<ArgumentException>(() => new AuditActor("42", " "));
    }

    [Fact]
    public void AuditInfo_requires_created_by()
    {
        Assert.Throws<ArgumentNullException>(() => new AuditInfo(DateTimeOffset.UtcNow, null!));
    }

    [Fact]
    public void AuditInfo_defaults_last_modified_fields_to_null()
    {
        var actor = new AuditActor("42", "user");
        var createdAt = DateTimeOffset.UtcNow;
        var audit = new AuditInfo(createdAt, actor);

        Assert.Equal(createdAt, audit.CreatedAt);
        Assert.Equal(actor, audit.CreatedBy);
        Assert.Null(audit.LastModifiedAt);
        Assert.Null(audit.LastModifiedBy);
    }

    [Fact]
    public void AuditInfo_tracks_last_modified_actor()
    {
        var actor = new AuditActor("42", "user");
        var audit = new AuditInfo(DateTimeOffset.UtcNow, actor, DateTimeOffset.UtcNow.AddMinutes(5), actor);

        Assert.Equal(actor, audit.LastModifiedBy);
    }
}

public class ExceptionEdgeTests
{
    [Fact]
    public void DomainException_preserves_inner_exception()
    {
        var inner = new InvalidOperationException("inner");
        var exception = new DomainException("outer", inner);

        Assert.Same(inner, exception.InnerException);
    }

    [Fact]
    public void SecurityException_preserves_inner_exception()
    {
        var inner = new InvalidOperationException("inner");
        var exception = new SecurityException("security", inner);

        Assert.Same(inner, exception.InnerException);
    }

    [Fact]
    public void SecurityException_accepts_message_only()
    {
        var exception = new SecurityException("security");

        Assert.Equal("security", exception.Message);
    }

    [Fact]
    public void ValidationException_defaults_errors_when_only_message()
    {
        var exception = new ValidationException("invalid");

        Assert.Empty(exception.Errors);
    }

    [Fact]
    public void ValidationException_accepts_null_errors()
    {
        var exception = new ValidationException("invalid", (IEnumerable<string>)null!);

        Assert.Empty(exception.Errors);
    }

    [Fact]
    public void ValidationException_accepts_null_validation_result()
    {
        var exception = new ValidationException("invalid", (ValidationResult)null!);

        Assert.Empty(exception.Errors);
    }

    [Fact]
    public void ValidationException_uses_errors_from_validation_result()
    {
        var validationResult = new ValidationResult(new[] { "A", "B" });
        var exception = new ValidationException("invalid", validationResult);

        Assert.Equal(validationResult.Errors, exception.Errors);
    }

    [Fact]
    public void ValidationException_uses_errors_from_enumerable()
    {
        var exception = new ValidationException("invalid", new[] { "A", "B" });

        Assert.Equal(2, exception.Errors.Count);
    }
}

public class DomainEdgeTests
{
    [Fact]
    public void Entity_creates_new_identifier_when_empty_is_provided()
    {
        var entity = new TestEntity(Guid.Empty);

        Assert.NotEqual(Guid.Empty, entity.Id);
    }

    [Fact]
    public void Entity_default_constructor_creates_identifier()
    {
        var entity = new TestEntity();

        Assert.NotEqual(Guid.Empty, entity.Id);
    }

    [Fact]
    public void DomainEvent_sets_identifier_and_timestamp()
    {
        var domainEvent = new TestDomainEvent();

        Assert.True(typeof(DomainEvent).IsAbstract);
        Assert.NotEqual(Guid.Empty, domainEvent.EventId);
        Assert.NotEqual(default, domainEvent.OccurredOn);
    }

    [Fact]
    public void Entity_equals_returns_true_for_reference()
    {
        var entity = new TestEntity(Guid.NewGuid());

        Assert.True(entity.Equals(entity));
    }

    [Fact]
    public void Entity_equals_returns_false_for_non_entity()
    {
        var entity = new TestEntity(Guid.NewGuid());

        Assert.False(entity.Equals("entity"));
    }

    [Fact]
    public void Entity_equality_operator_handles_nulls()
    {
        TestEntity? left = null;
        TestEntity? right = null;

        Assert.True(left == right);
    }

    [Fact]
    public void AggregateRoot_accepts_identifier()
    {
        var id = Guid.NewGuid();
        var aggregate = new TestAggregate(id);

        Assert.Equal(id, aggregate.Id);
    }

    [Fact]
    public void AggregateRoot_throws_when_event_is_null()
    {
        var aggregate = new TestAggregate();

        Assert.Throws<ArgumentNullException>(() => aggregate.Raise(null!));
    }

    [Fact]
    public void AggregateRoot_tracks_and_clears_domain_events()
    {
        var aggregate = new TestAggregate();
        var domainEvent = new TestDomainEvent();

        aggregate.Raise(domainEvent);

        Assert.Single(aggregate.DomainEvents, domainEvent);

        aggregate.ClearDomainEvents();

        Assert.Empty(aggregate.DomainEvents);
    }

    [Fact]
    public void ValueObject_is_equal_when_components_match()
    {
        var left = new TestValueObject("alpha", 1);
        var right = new TestValueObject("alpha", 1);

        Assert.True(left == right);
        Assert.Equal(left, right);
    }

    [Fact]
    public void ValueObject_hash_code_matches_for_equal_components()
    {
        var left = new TestValueObject("alpha", 1);
        var right = new TestValueObject("alpha", 1);

        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void ValueObject_is_not_equal_when_components_differ()
    {
        var left = new TestValueObject("alpha", 1);
        var right = new TestValueObject("alpha", 2);

        Assert.NotEqual(left, right);
        Assert.True(left != right);
    }

    [Fact]
    public void ValueObject_is_not_equal_when_type_differs()
    {
        var left = new TestValueObject("alpha", 1);
        var right = new OtherValueObject("alpha", 1);

        Assert.False(left.Equals(right));
    }

    private sealed class TestEntity : Entity
    {
        public TestEntity()
        {
        }

        public TestEntity(Guid id)
            : base(id)
        {
        }
    }

    private sealed class TestAggregate : AggregateRoot
    {
        public TestAggregate()
        {
        }

        public TestAggregate(Guid id)
            : base(id)
        {
        }

        public void Raise(DomainEvent domainEvent) => AddDomainEvent(domainEvent);
    }

    private sealed record TestDomainEvent : DomainEvent;


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

    private sealed class OtherValueObject : ValueObject
    {
        public OtherValueObject(string name, int count)
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
}

public class SharedEdgeTests
{
    [Fact]
    public void Result_failure_throws_when_error_is_null()
    {
        Assert.Throws<ArgumentException>(() => Result<string>.Failure(null!));
    }

    [Fact]
    public void PagedResult_requires_items()
    {
        Assert.Throws<ArgumentNullException>(() => new PagedResult<string>(null!, 1, 1, 1));
    }

    [Fact]
    public void PagedResult_requires_page_number_greater_than_zero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PagedResult<string>(new[] { "a" }, 0, 1, 1));
    }

    [Fact]
    public void PagedResult_requires_page_size_greater_than_zero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PagedResult<string>(new[] { "a" }, 1, 0, 1));
    }

    [Fact]
    public void PagedResult_requires_non_negative_total_count()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PagedResult<string>(new[] { "a" }, 1, 1, -1));
    }

    [Fact]
    public void PagedResult_has_no_next_or_previous_page_for_single_page()
    {
        var result = new PagedResult<string>(new[] { "a" }, 1, 10, 1);

        Assert.False(result.HasNextPage);
        Assert.False(result.HasPreviousPage);
    }
}
