using System.Security.Cryptography;
using System.Text;
using LlmHub.Infrastructure.Artifacts;

namespace LlmHub.IntegrationTests.Artifacts;

public sealed class S3ArtifactStoreTests
{
    [Fact]
    public async Task PersistsAndReadsContentFromLocalS3CompatibleStorage()
    {
        var bucket = $"llmhub-test-{Guid.NewGuid():N}";
        using var store = new S3ArtifactStore(new ArtifactStorageOptions("localhost:9000", "llmhub", "local-development-only", bucket));
        var content = Encoding.UTF8.GetBytes("artifact content");
        var expectedHash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

        var artifact = await store.PutAsync(content, "text/plain", expectedHash, CancellationToken.None);
        var restored = await store.GetAsync(artifact.StorageKey, CancellationToken.None);

        Assert.Equal(expectedHash, artifact.ContentHash);
        Assert.Equal(content, restored);
    }

    [Fact]
    public async Task RejectsInvalidChecksumBeforeStorageAccess()
    {
        using var store = new S3ArtifactStore(new ArtifactStorageOptions("localhost:9000", "llmhub", "local-development-only", "llmhub-artifacts"));

        await Assert.ThrowsAsync<ArtifactValidationException>(() => store.PutAsync(Encoding.UTF8.GetBytes("artifact"), "text/plain", "bad-checksum", CancellationToken.None));
    }
}
