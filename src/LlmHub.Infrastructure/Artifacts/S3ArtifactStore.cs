using System.Security.Cryptography;
using Minio;
using Minio.DataModel.Args;

namespace LlmHub.Infrastructure.Artifacts;

public sealed record ArtifactStorageOptions(string Endpoint, string AccessKey, string SecretKey, string Bucket = "llm-hub-artifacts", bool UseSsl = false);

public sealed record StoredArtifact(string StorageKey, string ContentHash, string ContentType, long Length);

public interface IArtifactStore
{
    Task<StoredArtifact> PutAsync(ReadOnlyMemory<byte> content, string contentType, string? expectedSha256, CancellationToken cancellationToken);

    Task<byte[]> GetAsync(string storageKey, CancellationToken cancellationToken);
}

public sealed class S3ArtifactStore : IArtifactStore, IDisposable
{
    private const int MaximumArtifactBytes = 20 * 1024 * 1024;
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/json",
        "application/octet-stream",
        "text/markdown",
        "text/plain",
    };

    private readonly IMinioClient client;
    private readonly ArtifactStorageOptions options;
    private readonly SemaphoreSlim bucketLock = new(1, 1);
    private bool bucketReady;

    public S3ArtifactStore(ArtifactStorageOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.AccessKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.SecretKey);
        this.options = options;
        IMinioClient builder = new MinioClient()
            .WithEndpoint(options.Endpoint)
            .WithCredentials(options.AccessKey, options.SecretKey);
        if (options.UseSsl)
        {
            builder = builder.WithSSL(true);
        }

        client = builder.Build();
    }

    public async Task<StoredArtifact> PutAsync(ReadOnlyMemory<byte> content, string contentType, string? expectedSha256, CancellationToken cancellationToken)
    {
        if (content.Length is 0 or > MaximumArtifactBytes)
        {
            throw new ArtifactValidationException("Artifact content must be between 1 byte and 20 MiB.");
        }

        if (!AllowedContentTypes.Contains(contentType))
        {
            throw new ArtifactValidationException("Artifact content type is not allowed.");
        }

        var hash = Convert.ToHexString(SHA256.HashData(content.Span)).ToLowerInvariant();
        if (expectedSha256 is not null && !CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(hash),
                System.Text.Encoding.ASCII.GetBytes(expectedSha256.ToLowerInvariant())))
        {
            throw new ArtifactValidationException("Artifact checksum does not match its content.");
        }

        await EnsureBucketAsync(cancellationToken);
        var storageKey = $"sha256/{hash}";
        await using var stream = new MemoryStream(content.ToArray(), writable: false);
        await client.PutObjectAsync(new PutObjectArgs()
            .WithBucket(options.Bucket)
            .WithObject(storageKey)
            .WithStreamData(stream)
            .WithObjectSize(content.Length)
            .WithContentType(contentType), cancellationToken);
        return new StoredArtifact(storageKey, hash, contentType, content.Length);
    }

    public async Task<byte[]> GetAsync(string storageKey, CancellationToken cancellationToken)
    {
        await EnsureBucketAsync(cancellationToken);
        await using var destination = new MemoryStream();
        await client.GetObjectAsync(new GetObjectArgs()
            .WithBucket(options.Bucket)
            .WithObject(storageKey)
            .WithCallbackStream(stream => stream.CopyTo(destination)), cancellationToken);
        return destination.ToArray();
    }

    private async Task EnsureBucketAsync(CancellationToken cancellationToken)
    {
        if (bucketReady)
        {
            return;
        }

        await bucketLock.WaitAsync(cancellationToken);
        try
        {
            if (!await client.BucketExistsAsync(new BucketExistsArgs().WithBucket(options.Bucket), cancellationToken))
            {
                await client.MakeBucketAsync(new MakeBucketArgs().WithBucket(options.Bucket), cancellationToken);
            }

            bucketReady = true;
        }
        finally
        {
            bucketLock.Release();
        }
    }

    public void Dispose() => bucketLock.Dispose();
}

public sealed class ArtifactValidationException(string message) : InvalidOperationException(message);
