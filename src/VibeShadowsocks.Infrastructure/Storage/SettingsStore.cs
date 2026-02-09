using System.Text.Json;
using Microsoft.Extensions.Logging;
using VibeShadowsocks.Core.Abstractions;
using VibeShadowsocks.Core.Models;
using VibeShadowsocks.Infrastructure.Options;

namespace VibeShadowsocks.Infrastructure.Storage;

public sealed class SettingsStore(ILogger<SettingsStore> logger, AppPaths paths) : ISettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly ILogger<SettingsStore> _logger = logger;
    private readonly AppPaths _paths = paths;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_paths.SettingsPath))
            {
                var defaults = CreateDefaultSettings();
                await SaveInternalAsync(defaults, cancellationToken).ConfigureAwait(false);
                return defaults;
            }

            await using var stream = File.OpenRead(_paths.SettingsPath);
            var loaded = await JsonSerializer.DeserializeAsync<AppSettings>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
            if (loaded is null)
            {
                throw new InvalidDataException("Settings file is empty or malformed.");
            }

            var migrated = Migrate(loaded);
            if (!ReferenceEquals(migrated, loaded) || migrated.SchemaVersion != loaded.SchemaVersion)
            {
                await SaveInternalAsync(migrated, cancellationToken).ConfigureAwait(false);
            }

            return migrated;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to load settings, fallback to defaults.");
            return CreateDefaultSettings();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var normalized = settings with
            {
                SchemaVersion = AppSettings.CurrentSchemaVersion,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };

            await SaveInternalAsync(normalized, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await LoadInternalUnsafeAsync(cancellationToken).ConfigureAwait(false);
            var updated = update(current) with
            {
                SchemaVersion = AppSettings.CurrentSchemaVersion,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };

            await SaveInternalAsync(updated, cancellationToken).ConfigureAwait(false);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<AppSettings> LoadInternalUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_paths.SettingsPath))
        {
            return CreateDefaultSettings();
        }

        await using var stream = File.OpenRead(_paths.SettingsPath);
        var loaded = await JsonSerializer.DeserializeAsync<AppSettings>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        return loaded is null ? CreateDefaultSettings() : Migrate(loaded);
    }

    private async Task SaveInternalAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.SettingsPath)!);
        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        await AtomicFileWriter.WriteTextAsync(_paths.SettingsPath, json, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static AppSettings Migrate(AppSettings loaded)
    {
        var migrated = loaded;

        if (migrated.SchemaVersion <= 0)
        {
            migrated = migrated with { SchemaVersion = 1 };
        }

        if (migrated.SchemaVersion < AppSettings.CurrentSchemaVersion)
        {
            migrated = migrated with { SchemaVersion = AppSettings.CurrentSchemaVersion };
        }

        return migrated;
    }

    private static AppSettings CreateDefaultSettings()
    {
        var defaultPac = new PacProfile
        {
            Name = "Managed default",
            Type = PacProfileType.Managed,
            InlineRules = "||example.com\n@@||intranet.local",
        };

        return new AppSettings
        {
            ActivePacProfileId = defaultPac.Id,
            PacProfiles = [defaultPac],
        };
    }
}
