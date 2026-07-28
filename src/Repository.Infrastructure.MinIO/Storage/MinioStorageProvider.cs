using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace Aletheia.Repository.Infrastructure.MinIO.Storage;

public sealed class MinioStorageProvider : IStorageProvider
{
    private const string UploadFailedMessage = "Upload failed.";
    private const string DownloadFailedMessage = "Download failed.";
    private const string DeleteFailedMessage = "Delete failed.";

    private readonly IMinioClient _minioClient;
    private readonly string _bucketName;

    public MinioStorageProvider(IMinioClient minioClient, string bucketName)
    {
        _minioClient = minioClient ?? throw new ArgumentNullException(nameof(minioClient));
        _bucketName = bucketName ?? throw new ArgumentNullException(nameof(bucketName));
    }

    public async Task<Result<UploadResponse>> UploadAsync(UploadRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        try
        {
            var objectName = GetObjectName(request.Descriptor);
            var beArgs = new BucketExistsArgs().WithBucket(_bucketName);
            var bucketExists = await _minioClient.BucketExistsAsync(beArgs).ConfigureAwait(false);
            if (!bucketExists)
            {
                var mbArgs = new MakeBucketArgs().WithBucket(_bucketName);
                await _minioClient.MakeBucketAsync(mbArgs).ConfigureAwait(false);
            }

            var putArgs = new PutObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(objectName)
                .WithStreamData(request.Content)
                .WithObjectSize(request.SizeBytes)
                .WithContentType(request.ContentType);

            await _minioClient.PutObjectAsync(putArgs, cancellationToken).ConfigureAwait(false);

            var metadata = new FileMetadata(
                request.Descriptor,
                request.ContentType,
                request.SizeBytes,
                DateTimeOffset.UtcNow,
                request.Tags);

            return Result<UploadResponse>.Success(new UploadResponse(metadata));
        }
        catch (Exception ex)
        {
            return Result<UploadResponse>.Failure(
                $"{UploadFailedMessage} Storage provider: MinIO. Bucket: {_bucketName}. File: {request.Descriptor.FileName}. SizeBytes: {request.SizeBytes}. Exception: {ex.GetType().Name}. Message: {ex.Message}");
        }
    }

    public async Task<Result<DownloadResponse>> DownloadAsync(DownloadRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var objectName = GetObjectName(request.Descriptor);
        var stream = new MemoryStream();

        var getArgs = new GetObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(objectName)
            .WithCallbackStream(s => s.CopyToAsync(stream));

        var objectStat = await _minioClient.GetObjectAsync(getArgs, cancellationToken).ConfigureAwait(false);
        stream.Position = 0;

        var metadata = new FileMetadata(
            request.Descriptor,
            objectStat.ContentType,
            objectStat.Size,
            DateTimeOffset.UtcNow);

        return Result<DownloadResponse>.Success(new DownloadResponse(metadata, stream));
    }

    public async Task<Result> DeleteAsync(DeleteRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var objectName = GetObjectName(request.Descriptor);
        var args = new RemoveObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(objectName);

        try
        {
            await _minioClient.RemoveObjectAsync(args, cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (ObjectNotFoundException)
        {
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"{DeleteFailedMessage} {ex.Message}");
        }
    }

    private static string GetObjectName(FileDescriptor descriptor)
    {
        return $"{descriptor.FileId}/{descriptor.FileName}";
    }
}
