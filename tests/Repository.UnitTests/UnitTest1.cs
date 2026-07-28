using Aletheia.Foundation.Audit;
using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Models;

namespace Repository.UnitTests.Models;

public class FileDescriptorTests
{
    [Fact]
    public void Constructor_sets_properties_when_valid()
    {
        var fileId = Guid.NewGuid();

        var descriptor = new FileDescriptor(fileId, "report.pdf", "v1");

        Assert.Equal(fileId, descriptor.FileId);
        Assert.Equal("report.pdf", descriptor.FileName);
        Assert.Equal("v1", descriptor.Version);
    }

    [Fact]
    public void Constructor_sets_version_to_null_when_whitespace()
    {
        var descriptor = new FileDescriptor(Guid.NewGuid(), "report.pdf", " ");

        Assert.Null(descriptor.Version);
    }

    [Fact]
    public void Constructor_throws_when_file_id_is_empty()
    {
        Assert.Throws<ArgumentException>(() => new FileDescriptor(Guid.Empty, "report.pdf"));
    }

    [Fact]
    public void Constructor_throws_when_file_name_is_missing()
    {
        Assert.Throws<ArgumentException>(() => new FileDescriptor(Guid.NewGuid(), " "));
    }
}

public class FileMetadataTests
{
    [Fact]
    public void Constructor_sets_properties_and_copies_tags()
    {
        var descriptor = new FileDescriptor(Guid.NewGuid(), "report.pdf", "v2");
        var tags = new Dictionary<string, string> { ["team"] = "data" };
        var uploadedAt = DateTimeOffset.UtcNow;

        var metadata = new FileMetadata(descriptor, "application/pdf", 128, uploadedAt, tags);

        tags["team"] = "updated";

        Assert.Equal(descriptor, metadata.Descriptor);
        Assert.Equal("application/pdf", metadata.ContentType);
        Assert.Equal(128, metadata.SizeBytes);
        Assert.Equal(uploadedAt, metadata.UploadedAt);
        Assert.Equal("data", metadata.Tags["team"]);
    }

    [Fact]
    public void Constructor_sets_empty_tags_when_null()
    {
        var descriptor = new FileDescriptor(Guid.NewGuid(), "report.pdf");

        var metadata = new FileMetadata(descriptor, "application/pdf", 0, DateTimeOffset.UtcNow);

        Assert.Empty(metadata.Tags);
    }

    [Fact]
    public void Constructor_throws_when_descriptor_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => new FileMetadata(null!, "application/pdf", 0, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Constructor_sets_audit_info_when_provided()
    {
        var descriptor = new FileDescriptor(Guid.NewGuid(), "report.pdf");
        var actor = new AuditActor("42", "user");
        var audit = new AuditInfo(DateTimeOffset.UtcNow, actor);

        var metadata = new FileMetadata(descriptor, "application/pdf", 1, DateTimeOffset.UtcNow, auditInfo: audit);

        Assert.Equal(actor, metadata.AuditInfo?.CreatedBy);
    }

    [Fact]
    public void Constructor_throws_when_content_type_is_missing()
    {
        var descriptor = new FileDescriptor(Guid.NewGuid(), "report.pdf");

        Assert.Throws<ArgumentException>(() => new FileMetadata(descriptor, " ", 0, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Constructor_throws_when_size_is_negative()
    {
        var descriptor = new FileDescriptor(Guid.NewGuid(), "report.pdf");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FileMetadata(descriptor, "application/pdf", -1, DateTimeOffset.UtcNow));
    }
}

public class UploadRequestTests
{
    [Fact]
    public void Constructor_sets_properties_and_copies_tags()
    {
        var descriptor = new FileDescriptor(Guid.NewGuid(), "report.pdf");
        var tags = new Dictionary<string, string> { ["team"] = "data" };
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        var request = new UploadRequest(descriptor, stream, "application/pdf", 3, tags);

        tags["team"] = "updated";

        Assert.Equal(descriptor, request.Descriptor);
        Assert.Equal(stream, request.Content);
        Assert.Equal("application/pdf", request.ContentType);
        Assert.Equal(3, request.SizeBytes);
        Assert.Equal("data", request.Tags["team"]);
    }

    [Fact]
    public void Constructor_throws_when_content_stream_is_not_readable()
    {
        var descriptor = new FileDescriptor(Guid.NewGuid(), "report.pdf");
        using var stream = new NonReadableStream();

        Assert.Throws<ArgumentException>(() =>
            new UploadRequest(descriptor, stream, "application/pdf", 0));
    }

    [Fact]
    public void Constructor_throws_when_content_type_is_missing()
    {
        var descriptor = new FileDescriptor(Guid.NewGuid(), "report.pdf");
        using var stream = new MemoryStream();

        Assert.Throws<ArgumentException>(() => new UploadRequest(descriptor, stream, " ", 0));
    }

    [Fact]
    public void Constructor_throws_when_size_is_negative()
    {
        var descriptor = new FileDescriptor(Guid.NewGuid(), "report.pdf");
        using var stream = new MemoryStream();

        Assert.Throws<ArgumentOutOfRangeException>(() => new UploadRequest(descriptor, stream, "application/pdf", -1));
    }

    [Fact]
    public void Constructor_throws_when_descriptor_is_null()
    {
        using var stream = new MemoryStream();

        Assert.Throws<ArgumentNullException>(() => new UploadRequest(null!, stream, "application/pdf", 0));
    }

    [Fact]
    public void Constructor_throws_when_content_is_null()
    {
        var descriptor = new FileDescriptor(Guid.NewGuid(), "report.pdf");

        Assert.Throws<ArgumentNullException>(() => new UploadRequest(descriptor, null!, "application/pdf", 0));
    }

    [Fact]
    public void Constructor_sets_empty_tags_when_null()
    {
        var descriptor = new FileDescriptor(Guid.NewGuid(), "report.pdf");
        using var stream = new MemoryStream();

        var request = new UploadRequest(descriptor, stream, "application/pdf", 0);

        Assert.Empty(request.Tags);
    }


}

public class UploadResponseTests
{
    [Fact]
    public void Constructor_sets_properties_when_valid()
    {
        var descriptor = new FileDescriptor(Guid.NewGuid(), "report.pdf");
        var metadata = new FileMetadata(descriptor, "application/pdf", 3, DateTimeOffset.UtcNow);

        var response = new UploadResponse(metadata);

        Assert.Equal(metadata, response.Metadata);
    }

    [Fact]
    public void Constructor_throws_when_metadata_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => new UploadResponse(null!));
    }
}

public class DownloadRequestTests
{
    [Fact]
    public void Constructor_throws_when_descriptor_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => new DownloadRequest(null!));
    }

    [Fact]
    public void Constructor_sets_properties_when_valid()
    {
        var descriptor = new FileDescriptor(Guid.NewGuid(), "report.pdf");

        var request = new DownloadRequest(descriptor);

        Assert.Equal(descriptor, request.Descriptor);
    }
}

public class DownloadResponseTests
{
    [Fact]
    public void Constructor_sets_properties_when_valid()
    {
        var descriptor = new FileDescriptor(Guid.NewGuid(), "report.pdf");
        var metadata = new FileMetadata(descriptor, "application/pdf", 3, DateTimeOffset.UtcNow);
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        var response = new DownloadResponse(metadata, stream);

        Assert.Equal(metadata, response.Metadata);
        Assert.Equal(stream, response.Content);
    }

    [Fact]
    public void Constructor_throws_when_metadata_is_null()
    {
        using var stream = new MemoryStream(new byte[] { 1 });

        Assert.Throws<ArgumentNullException>(() => new DownloadResponse(null!, stream));
    }

    [Fact]
    public void Constructor_throws_when_stream_is_null()
    {
        var descriptor = new FileDescriptor(Guid.NewGuid(), "report.pdf");
        var metadata = new FileMetadata(descriptor, "application/pdf", 3, DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentNullException>(() => new DownloadResponse(metadata, null!));
    }

    [Fact]
    public void Constructor_throws_when_stream_is_not_readable()
    {
        var descriptor = new FileDescriptor(Guid.NewGuid(), "report.pdf");
        var metadata = new FileMetadata(descriptor, "application/pdf", 3, DateTimeOffset.UtcNow);
        using var stream = new NonReadableStream();

        Assert.Throws<ArgumentException>(() => new DownloadResponse(metadata, stream));
    }
}

public class SearchRequestTests
{
    [Fact]
    public void Constructor_sets_properties_and_copies_filters()
    {
        var filters = new Dictionary<string, string> { ["category"] = "reports" };

        var request = new SearchRequest("  query ", 1, 25, filters);

        filters["category"] = "updated";

        Assert.Equal("  query ", request.Query);
        Assert.Equal(1, request.PageNumber);
        Assert.Equal(25, request.PageSize);
        Assert.Equal("reports", request.Filters["category"]);
    }

    [Fact]
    public void Constructor_sets_query_to_null_when_blank()
    {
        var request = new SearchRequest("  ", 1, 10);

        Assert.Null(request.Query);
    }

    [Fact]
    public void Constructor_throws_when_page_number_is_invalid()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SearchRequest("query", 0, 10));
    }

    [Fact]
    public void Constructor_throws_when_page_size_is_invalid()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SearchRequest("query", 1, 0));
    }
}

public class SearchResponseTests
{
    [Fact]
    public void Constructor_sets_results_when_valid()
    {
        var descriptor = new FileDescriptor(Guid.NewGuid(), "report.pdf");
        var metadata = new FileMetadata(descriptor, "application/pdf", 3, DateTimeOffset.UtcNow);
        var results = new PagedResult<FileMetadata>(new[] { metadata }, 1, 10, 1);

        var response = new SearchResponse(results);

        Assert.Equal(results, response.Results);
    }

    [Fact]
    public void Constructor_throws_when_results_are_null()
    {
        Assert.Throws<ArgumentNullException>(() => new SearchResponse(null!));
    }
}

internal sealed class NonReadableStream : Stream
{
    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}