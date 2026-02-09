using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VibeShadowsocks.Infrastructure.Options;
using VibeShadowsocks.Infrastructure.Storage;

namespace VibeShadowsocks.Infrastructure.Networking;

public sealed class HttpCache(ILogger<HttpCache> logger, IHttpClientFactory httpClientFactory, AppPaths paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly ILogger<HttpCache> _logger = logger;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly AppPaths _paths = paths;

    public async Task<HttpCacheResult> GetStringAsync(
        Uri uri,
        string cacheKey,
        long maxBytes,
        CancellationToken cancellationToken = default)
    {
        EnsureHttps(uri);

        var cachePaths = GetCachePaths(cacheKey);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePaths.BodyPath)!);

        var metadata = await ReadMetadataAsync(cachePaths.MetadataPath, cancellationToken).ConfigureAwait(false);
        var retries = 3;
        var delay = TimeSpan.FromMilliseconds(300);
        Exception? lastException = null;
        for (var attempt = 1; attempt <= retries; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                if (!string.IsNullOrWhiteSpace(metadata?.ETag))
                {
                    request.Headers.TryAddWithoutValidation("If-None-Match", metadata.ETag);
                }

                if (metadata?.LastModified is { } lastModified)
                {
                    request.Headers.IfModifiedSince = lastModified;
                }

                var client = _httpClientFactory.CreateClient(nameof(HttpCache));
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.NotModified)
                {
                    var cached = await ReadCachedBodyAsync(cachePaths.BodyPath, cancellationToken).ConfigureAwait(false);
                    if (cached is null)
                    {
                        throw new InvalidOperationException("Received 304 but cache body is missing.");
                    }

                    return new HttpCacheResult(
                        cached,
                        FromCache: true,
                        metadata?.ETag,
                        metadata?.LastModified,
                        metadata?.ContentType,
                        DateTimeOffset.UtcNow);
                }

                response.EnsureSuccessStatusCode();
                var contentType = response.Content.Headers.ContentType?.MediaType;
                var bytes = await ReadLimitedBytesAsync(response, maxBytes, cancellationToken).ConfigureAwait(false);
                var body = Encoding.UTF8.GetString(bytes);

                var newMetadata = new CacheMetadata(
                    response.Headers.ETag?.Tag,
                    response.Content.Headers.LastModified,
                    contentType,
                    DateTimeOffset.UtcNow);

                await AtomicFileWriter.WriteBytesAsync(cachePaths.BodyPath, bytes, cancellationToken).ConfigureAwait(false);
                await AtomicFileWriter.WriteTextAsync(cachePaths.MetadataPath, JsonSerializer.Serialize(newMetadata, JsonOptions), cancellationToken: cancellationToken).ConfigureAwait(false);

                return new HttpCacheResult(body, FromCache: false, newMetadata.ETag, newMetadata.LastModified, contentType, newMetadata.RetrievedAtUtc);
            }
            catch (Exception exception)
            {
                lastException = exception;
                if (attempt < retries)
                {
                    _logger.LogWarning(exception, "HTTP fetch attempt {Attempt}/{MaxAttempts} failed for {Uri}", attempt, retries, uri);
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);
                }
            }
        }

        var fallback = await ReadCachedBodyAsync(cachePaths.BodyPath, cancellationToken).ConfigureAwait(false);
        if (fallback is not null)
        {
            _logger.LogWarning("Using stale cache for {Uri}", uri);
            return new HttpCacheResult(
                fallback,
                FromCache: true,
                metadata?.ETag,
                metadata?.LastModified,
                metadata?.ContentType,
                DateTimeOffset.UtcNow);
        }

        throw new InvalidOperationException($"Unable to fetch resource: {uri}", lastException);
    }

    private static void EnsureHttps(Uri uri)
    {
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only HTTPS sources are allowed.");
        }
    }

    private async Task<CacheMetadata?> ReadMetadataAsync(string metadataPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(metadataPath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<CacheMetadata>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> ReadCachedBodyAsync(string bodyPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(bodyPath))
        {
            return null;
        }

        return await File.ReadAllTextAsync(bodyPath, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadLimitedBytesAsync(HttpResponseMessage response, long maxBytes, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > 0 and var length && length > maxBytes)
        {
            throw new InvalidOperationException($"Response exceeds max allowed size ({maxBytes} bytes).");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];
        long total = 0;

        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > maxBytes)
            {
                throw new InvalidOperationException($"Response exceeds max allowed size ({maxBytes} bytes).");
            }

            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return memory.ToArray();
    }

    private (string BodyPath, string MetadataPath) GetCachePaths(string cacheKey)
    {
        var safeKey = ToSafeFileName(cacheKey);
        var directory = Path.Combine(_paths.CacheDirectory, "http");
        return (
            Path.Combine(directory, $"{safeKey}.body"),
            Path.Combine(directory, $"{safeKey}.meta.json"));
    }

    private static string ToSafeFileName(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
