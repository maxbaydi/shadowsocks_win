namespace VibeShadowsocks.Infrastructure.Networking;

public sealed record HttpCacheResult(
    string Content,
    bool FromCache,
    string? ETag,
    DateTimeOffset? LastModified,
    string? ContentType,
    DateTimeOffset RetrievedAtUtc);

internal sealed record CacheMetadata(
    string? ETag,
    DateTimeOffset? LastModified,
    string? ContentType,
    DateTimeOffset RetrievedAtUtc);
