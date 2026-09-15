namespace Pi1.HyperVToolkit.Core.Services;

/// <summary>
/// Central application-data locations. Parity intent: the PowerShell toolkit
/// writes Logs\ next to the script; the C# application must NOT require write
/// access to the executable directory and uses %LocalAppData%\Pi1\HyperVToolkit\.
/// </summary>
public interface IAppPaths
{
    string AppDataDirectory { get; }
    string LogsDirectory { get; }
    string SettingsFilePath { get; }

    /// <summary>Creates the directories when missing. Safe to call repeatedly.</summary>
    void EnsureCreated();
}
