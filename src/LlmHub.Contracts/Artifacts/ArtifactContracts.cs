namespace LlmHub.Contracts.Artifacts;

public sealed record ArtifactReference(string StorageKey, string ContentHash, string ContentType, long Length);
