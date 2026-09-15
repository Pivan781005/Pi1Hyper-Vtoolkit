using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Services;

namespace Pi1.HyperVToolkit.Infrastructure.Capabilities;

/// <summary>
/// Startup capability probes (native WMI/CIM only — no PowerShell hosting).
/// Mirrors Test-HyperVModule (hard gate) and Test-PiClusterModule (soft gate).
/// Probes never throw; failures are returned as unavailable reports and logged.
/// Full inventory backends arrive in later migration phases.
/// </summary>
public sealed class CapabilityProbeService : ICapabilityProbeService
{
    private const string HyperVNamespace = @"\\.\root\virtualization\v2";
    private const string ClusterNamespace = @"\\.\root\MSCluster";
    private const string CimNamespace = @"\\.\root\cimv2";

    private readonly ILogger<CapabilityProbeService> _logger;

    public CapabilityProbeService(ILogger<CapabilityProbeService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<CapabilityReport> CheckHyperVAsync(CancellationToken cancellationToken = default) =>
        ProbeAsync(
            CapabilityKind.HyperV,
            HyperVNamespace,
            "Hyper-V: OK",
            "Hyper-V nie je dostupné. Spusti aplikáciu na Hyper-V hostiteľovi alebo na stanici s nástrojmi Hyper-V.",
            cancellationToken);

    public Task<CapabilityReport> CheckClusterAsync(CancellationToken cancellationToken = default) =>
        ProbeAsync(
            CapabilityKind.FailoverCluster,
            ClusterNamespace,
            "Cluster: OK",
            "Failover Cluster nástroje neboli nájdené. Funkcie klastra budú dostupné len na uzle klastra alebo so sadou RSAT Failover Clustering Tools.",
            cancellationToken);

    public Task<CapabilityReport> CheckCimAsync(CancellationToken cancellationToken = default) =>
        ProbeAsync(
            CapabilityKind.Cim,
            CimNamespace,
            "CIM/WMI: OK",
            "CIM/WMI nie je dostupné. Over stav služby Windows Management Instrumentation.",
            cancellationToken);

    private Task<CapabilityReport> ProbeAsync(
        CapabilityKind kind,
        string scopePath,
        string okDetail,
        string missingDetail,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var scope = new ManagementScope(scopePath);
                scope.Connect();
                return new CapabilityReport(kind, true, okDetail);
            }
            catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException or InvalidOperationException)
            {
                _logger.LogDebug(ex, "Capability probe {Kind} nie je dostupná ({Scope}).", kind, scopePath);
                return new CapabilityReport(kind, false, missingDetail);
            }
        }, cancellationToken);
    }
}
