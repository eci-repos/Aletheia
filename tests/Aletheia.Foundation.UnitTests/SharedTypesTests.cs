using Aletheia.Foundation.Shared;

namespace Aletheia.Foundation.UnitTests;

public class SharedTypesTests
{
    [Fact]
    public void Result_SuccessSetsValue()
    {
        var result = Result<string>.Success("ok");

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal("ok", result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Result_FailureSetsError()
    {
        var result = Result<string>.Failure("error");

        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.Equal("error", result.Error);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Result_FailureRequiresMessage()
    {
        Assert.Throws<ArgumentException>(() => Result<string>.Failure(" "));
    }

    [Fact]
    public void PagedResult_ComputesPagingMetadata()
    {
        var items = new[] { "a", "b" };

        var result = new PagedResult<string>(items, pageNumber: 2, pageSize: 10, totalCount: 25);

        Assert.Equal(2, result.PageNumber);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(25, result.TotalCount);
        Assert.Equal(3, result.TotalPages);
        Assert.True(result.HasNextPage);
        Assert.True(result.HasPreviousPage);
        Assert.Equal(items, result.Items);
    }
}
