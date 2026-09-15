using Pi1.HyperVToolkit.Core.Services;

namespace Pi1.HyperVToolkit.Infrastructure.Paths;

/// <summary>
/// Application-data locations under %LocalAppData%\Pi1\HyperVToolkit\.
/// An explicit <paramref name="baseDirectory"/> override exists for tests only;
/// production code uses the parameterless constructor.
/// </summary>
public sealed class AppPaths : IAppPaths
{
    public AppPaths(string? baseDirectory = null)
    {
        AppDataDirectory = baseDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pi1", "HyperVToolkit");
        LogsDirectory = Path.Combine(AppDataDirectory, "Logs");
        SettingsFilePath = Path.Combine(AppDataDirectory, "settings.json");
    }

    public string AppDataDirectory { get; }
    public string LogsDirectory { get; }
    public string SettingsFilePath { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(AppDataDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
