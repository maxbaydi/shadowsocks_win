using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using VibeShadowsocks.Core.Abstractions;
using VibeShadowsocks.Infrastructure.Options;

namespace VibeShadowsocks.Infrastructure.Storage;

public sealed class SecureStorage(ILogger<SecureStorage> logger, AppPaths paths) : ISecureStorage
{
    private readonly ILogger<SecureStorage> _logger = logger;
    private readonly AppPaths _paths = paths;

    public async Task SaveSecretAsync(string secretId, string secretValue, CancellationToken cancellationToken = default)
    {
        ValidateSecretId(secretId);
        Directory.CreateDirectory(_paths.SecretsDirectory);

        var plaintext = Encoding.UTF8.GetBytes(secretValue);
        var protectedBytes = ProtectedData.Protect(plaintext, optionalEntropy: null, DataProtectionScope.CurrentUser);

        await AtomicFileWriter
            .WriteBytesAsync(GetSecretPath(secretId), protectedBytes, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<string?> ReadSecretAsync(string secretId, CancellationToken cancellationToken = default)
    {
        ValidateSecretId(secretId);

        var path = GetSecretPath(secretId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            var plaintext = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (CryptographicException exception)
        {
            _logger.LogError(exception, "Failed to decrypt secret {SecretId}", secretId);
            return null;
        }
    }

    public Task DeleteSecretAsync(string secretId, CancellationToken cancellationToken = default)
    {
        ValidateSecretId(secretId);
        var path = GetSecretPath(secretId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string GetSecretPath(string secretId) => Path.Combine(_paths.SecretsDirectory, $"{secretId}.bin");

    private static void ValidateSecretId(string secretId)
    {
        if (string.IsNullOrWhiteSpace(secretId) || secretId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Invalid secret id.", nameof(secretId));
        }
    }
}
