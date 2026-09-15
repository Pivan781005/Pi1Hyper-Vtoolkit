using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Services;

namespace Pi1.HyperVToolkit.Core.State;

/// <summary>JSON settings persistence. Missing or corrupt files yield <see cref="AppSettings.Default"/>.</summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
    };

    private readonly IAppPaths _paths;
    private readonly ILogger<SettingsService> _logger;

    public SettingsService(IAppPaths paths, ILogger<SettingsService> logger)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public AppSettings Load()
    {
        try
        {
            var file = _paths.SettingsFilePath;
            if (!File.Exists(file))
            {
                return AppSettings.Default;
            }

            var json = File.ReadAllText(file);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, ReadOptions);
            if (settings is null)
            {
                _logger.LogWarning("Súbor nastavení {File} je prázdny. Používam predvolené nastavenia.", file);
                return AppSettings.Default;
            }

            return settings with
            {
                SelectedNode = settings.SelectedNode ?? string.Empty,
                Language = settings.Language == "en" ? "en" : "sk",
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "Nepodarilo sa načítať nastavenia. Používam predvolené nastavenia.");
            return AppSettings.Default;
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _paths.EnsureCreated();
        var json = JsonSerializer.Serialize(settings, WriteOptions);
        await File.WriteAllTextAsync(_paths.SettingsFilePath, json, cancellationToken).ConfigureAwait(false);
    }
}
