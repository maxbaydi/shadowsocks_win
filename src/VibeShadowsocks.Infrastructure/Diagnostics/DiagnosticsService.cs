using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VibeShadowsocks.Core.Abstractions;
using VibeShadowsocks.Core.Models;
using VibeShadowsocks.Infrastructure.Options;

namespace VibeShadowsocks.Infrastructure.Diagnostics;

public sealed class DiagnosticsService(
    ILogger<DiagnosticsService> logger,
    ISettingsStore settingsStore,
    IConnectionOrchestrator orchestrator,
    AppPaths paths) : IDiagnosticsService
{
    private readonly ILogger<DiagnosticsService> _logger = logger;
    private readonly ISettingsStore _settingsStore = settingsStore;
    private readonly IConnectionOrchestrator _orchestrator = orchestrator;
    private readonly AppPaths _paths = paths;

    public async Task<DiagnosticsSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var logs = await ReadRecentLogLinesAsync(settings.DiagnosticsLogTailLines, cancellationToken).ConfigureAwait(false);
        var assemblyVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";

        return new DiagnosticsSnapshot
        {
            AppVersion = assemblyVersion,
            OsVersion = Environment.OSVersion.VersionString,
            RuntimeVersion = Environment.Version.ToString(),
            CurrentState = _orchestrator.Snapshot.State.ToString(),
            ActiveProfileId = settings.ActiveServerProfileId ?? string.Empty,
            ActivePacProfileId = settings.ActivePacProfileId ?? string.Empty,
            SocksPort = settings.Ports.SocksPort,
            PacPort = settings.Ports.PacServerPort,
            RecentLogLines = logs,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    public async Task<string> BuildTextReportAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await CaptureAsync(cancellationToken).ConfigureAwait(false);
        var builder = new StringBuilder();
        builder.AppendLine($"GeneratedAtUtc: {snapshot.CreatedAtUtc:O}");
        builder.AppendLine($"AppVersion: {snapshot.AppVersion}");
        builder.AppendLine($"OS: {snapshot.OsVersion}");
        builder.AppendLine($"Runtime: {snapshot.RuntimeVersion}");
        builder.AppendLine($"State: {snapshot.CurrentState}");
        builder.AppendLine($"ActiveServerProfileId: {snapshot.ActiveProfileId}");
        builder.AppendLine($"ActivePacProfileId: {snapshot.ActivePacProfileId}");
        builder.AppendLine($"SocksPort: {snapshot.SocksPort}");
        builder.AppendLine($"PacPort: {snapshot.PacPort}");
        builder.AppendLine();
        builder.AppendLine("RecentLogs:");

        foreach (var line in snapshot.RecentLogLines)
        {
            builder.AppendLine(line);
        }

        return builder.ToString();
    }

    public async Task<string> ExportBundleAsync(string destinationDirectory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(destinationDirectory);
        Directory.CreateDirectory(_paths.LogsDirectory);

        var outputPath = Path.Combine(destinationDirectory, $"vibeshadowsocks-diagnostics-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.zip");
        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        using var zip = ZipFile.Open(outputPath, ZipArchiveMode.Create);

        if (File.Exists(_paths.SettingsPath))
        {
            var settings = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            var sanitizedJson = JsonSerializer.Serialize(settings, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
            var entry = zip.CreateEntry("settings.redacted.json");
            await using var stream = entry.Open();
            await using var writer = new StreamWriter(stream, Encoding.UTF8);
            await writer.WriteAsync(sanitizedJson).ConfigureAwait(false);
        }

        if (Directory.Exists(_paths.LogsDirectory))
        {
            foreach (var logPath in Directory.EnumerateFiles(_paths.LogsDirectory, "*.log", SearchOption.TopDirectoryOnly))
            {
                zip.CreateEntryFromFile(logPath, Path.Combine("logs", Path.GetFileName(logPath)));
            }
        }

        var reportText = await BuildTextReportAsync(cancellationToken).ConfigureAwait(false);
        var reportEntry = zip.CreateEntry("diagnostics.txt");
        await using (var stream = reportEntry.Open())
        await using (var writer = new StreamWriter(stream, Encoding.UTF8))
        {
            await writer.WriteAsync(reportText).ConfigureAwait(false);
        }

        _logger.LogInformation("Diagnostics bundle exported: {Path}", outputPath);
        return outputPath;
    }

    private async Task<IReadOnlyList<string>> ReadRecentLogLinesAsync(int maxLines, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_paths.LogsDirectory))
        {
            return [];
        }

        var latestLog = Directory
            .EnumerateFiles(_paths.LogsDirectory, "*.log", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (latestLog is null)
        {
            return [];
        }

        var allLines = await File.ReadAllLinesAsync(latestLog, cancellationToken).ConfigureAwait(false);
        return allLines.TakeLast(Math.Max(maxLines, 1)).ToArray();
    }
}
