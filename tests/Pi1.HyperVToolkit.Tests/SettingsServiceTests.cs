using Microsoft.Extensions.Logging.Abstractions;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.State;
using Pi1.HyperVToolkit.Infrastructure.Paths;

namespace Pi1.HyperVToolkit.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SettingsService _service;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Pi1Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(_tempDir);
        _service = new SettingsService(paths, NullLogger<SettingsService>.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort test cleanup.
        }
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefault()
    {
        var settings = _service.Load();
        Assert.Equal(AppSettings.Default, settings);
        Assert.Equal(ScopeMode.Local, settings.ScopeMode);
        Assert.True(settings.ShowLegend);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrips()
    {
        var expected = new AppSettings(ScopeMode.Node, "NODE2", ShowLegend: false);
        await _service.SaveAsync(expected);
        var actual = _service.Load();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsLanguage()
    {
        await _service.SaveAsync(new AppSettings(ScopeMode.Local, string.Empty, true, "en"));
        var actual = _service.Load();
        Assert.Equal("en", actual.Language);
        Assert.Equal(Core.Localization.AppLanguage.English, actual.AppLanguage);
    }

    [Fact]
    public void Load_LegacyFileWithoutLanguage_DefaultsToSlovak()
    {
        var paths = new AppPaths(_tempDir);
        paths.EnsureCreated();
        File.WriteAllText(paths.SettingsFilePath, """{"ScopeMode":0,"SelectedNode":"","ShowLegend":true}""");
        var actual = _service.Load();
        Assert.Equal("sk", actual.Language);
        Assert.Equal(Core.Localization.AppLanguage.Slovak, actual.AppLanguage);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefault()
    {
        var paths = new AppPaths(_tempDir);
        paths.EnsureCreated();
        File.WriteAllText(paths.SettingsFilePath, "{ this is not json");
        var actual = _service.Load();
        Assert.Equal(AppSettings.Default, actual);
    }

    [Fact]
    public void Load_EmptyFile_ReturnsDefault()
    {
        var paths = new AppPaths(_tempDir);
        paths.EnsureCreated();
        File.WriteAllText(paths.SettingsFilePath, string.Empty);
        var actual = _service.Load();
        Assert.Equal(AppSettings.Default, actual);
    }

    [Fact]
    public async Task SaveAsync_CreatesDirectory()
    {
        await _service.SaveAsync(AppSettings.Default);
        Assert.True(File.Exists(Path.Combine(_tempDir, "settings.json")));
    }
}
