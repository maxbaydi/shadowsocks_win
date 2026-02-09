using System.Text;

namespace VibeShadowsocks.Infrastructure.Storage;

public static class AtomicFileWriter
{
    public static async Task WriteTextAsync(string filePath, string content, Encoding? encoding = null, CancellationToken cancellationToken = default)
    {
        var bytes = (encoding ?? Encoding.UTF8).GetBytes(content);
        await WriteBytesAsync(filePath, bytes, cancellationToken).ConfigureAwait(false);
    }

    public static async Task WriteBytesAsync(string filePath, byte[] content, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(filePath) ?? throw new InvalidOperationException($"Invalid file path: {filePath}");
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $"{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllBytesAsync(tempPath, content, cancellationToken).ConfigureAwait(false);

        if (File.Exists(filePath))
        {
            File.Replace(tempPath, filePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tempPath, filePath);
        }
    }
}
