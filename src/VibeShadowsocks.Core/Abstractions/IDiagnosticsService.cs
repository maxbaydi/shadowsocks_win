using VibeShadowsocks.Core.Models;

namespace VibeShadowsocks.Core.Abstractions;

public interface IDiagnosticsService
{
    Task<DiagnosticsSnapshot> CaptureAsync(CancellationToken cancellationToken = default);

    Task<string> BuildTextReportAsync(CancellationToken cancellationToken = default);

    Task<string> ExportBundleAsync(string destinationDirectory, CancellationToken cancellationToken = default);
}
