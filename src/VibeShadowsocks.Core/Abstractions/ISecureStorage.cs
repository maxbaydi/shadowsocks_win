namespace VibeShadowsocks.Core.Abstractions;

public interface ISecureStorage
{
    Task SaveSecretAsync(string secretId, string secretValue, CancellationToken cancellationToken = default);

    Task<string?> ReadSecretAsync(string secretId, CancellationToken cancellationToken = default);

    Task DeleteSecretAsync(string secretId, CancellationToken cancellationToken = default);
}
