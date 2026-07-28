using Aletheia.Foundation.Audit;
using Aletheia.Foundation.Context;
using Aletheia.Foundation.Exceptions;
using Aletheia.Foundation.Validation;

namespace Aletheia.Foundation.UnitTests;

public class ValidationContextAuditTests
{
    [Fact]
    public void ValidationResult_StartsValidAndTracksErrors()
    {
        var result = new ValidationResult();

        Assert.True(result.IsValid);

        result.AddError("Missing name");

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void ValidationException_CapturesValidationErrors()
    {
        var validation = new ValidationResult(new[] { "Error A", "Error B" });

        var exception = new ValidationException("Invalid", validation);

        Assert.Equal(2, exception.Errors.Count);
    }

    [Fact]
    public void CorrelationContext_GeneratesIdentifier()
    {
        var context = CorrelationContext.New();

        Assert.NotEqual(Guid.Empty, context.CorrelationId);
    }

    [Fact]
    public void SecurityContext_TracksRoles()
    {
        var context = new SecurityContext("user-1", new[] { "Admin", "Reader" });

        Assert.True(context.HasRole("Admin"));
        Assert.False(context.HasRole("Writer"));
        Assert.Equal("user-1", context.UserId);
    }

    [Fact]
    public void TenantContext_RequiresTenantId()
    {
        var context = new TenantContext("tenant-1");

        Assert.Equal("tenant-1", context.TenantId);
    }

    [Fact]
    public void AuditInfo_HoldsActorDetails()
    {
        var actor = new AuditActor("42", "User", "Jordan");
        var audit = new AuditInfo(DateTimeOffset.UtcNow, actor);

        Assert.Equal("42", audit.CreatedBy.ActorId);
        Assert.Equal("User", audit.CreatedBy.ActorType);
        Assert.Equal("Jordan", audit.CreatedBy.DisplayName);
    }

    [Fact]
    public void DomainException_PreservesMessage()
    {
        var exception = new DomainException("broken");

        Assert.Equal("broken", exception.Message);
    }
}
