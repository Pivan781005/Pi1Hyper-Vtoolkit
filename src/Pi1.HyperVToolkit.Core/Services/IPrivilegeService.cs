namespace Pi1.HyperVToolkit.Core.Services;

/// <summary>Elevation state. The application manifest requires Administrator;
/// this service exposes the detected state for the status bar and diagnostics.</summary>
public interface IPrivilegeService
{
    bool IsAdministrator { get; }
    string ElevationLabel { get; }
}
