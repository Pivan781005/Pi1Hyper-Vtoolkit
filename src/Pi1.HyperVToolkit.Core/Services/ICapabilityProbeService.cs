using Pi1.HyperVToolkit.Core.Models;

namespace Pi1.HyperVToolkit.Core.Services;

/// <summary>
/// Startup capability probes. Infrastructure for the Test-HyperVModule /
/// Test-PiClusterModule gates. Reporting backends are a later phase; this phase
/// only detects and explains missing capabilities.
/// </summary>
public interface ICapabilityProbeService
{
    Task<CapabilityReport> CheckHyperVAsync(CancellationToken cancellationToken = default);
    Task<CapabilityReport> CheckClusterAsync(CancellationToken cancellationToken = default);
    Task<CapabilityReport> CheckCimAsync(CancellationToken cancellationToken = default);
}
