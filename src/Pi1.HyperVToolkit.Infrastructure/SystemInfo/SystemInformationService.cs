using Microsoft.Extensions.Logging;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Services;
using Pi1.HyperVToolkit.Infrastructure.Cim;
using Pi1.HyperVToolkit.Infrastructure.Common;
using Pi1.HyperVToolkit.Infrastructure.Mapping;

namespace Pi1.HyperVToolkit.Infrastructure.SystemInfo;

/// <summary>Native CIM inventory over root/cimv2 (no Hyper-V dependency).</summary>
public sealed class SystemInformationService : ISystemInformationService
{
    private const string Ns = @"root\cimv2";

    private readonly ICimQuerier _cim;
    private readonly ILogger<SystemInformationService> _logger;

    public SystemInformationService(ICimQuerier cim, ILogger<SystemInformationService> logger)
    {
        _cim = cim ?? throw new ArgumentNullException(nameof(cim));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<IReadOnlyList<NodeResult<NodeHardwareRow>>> GetNodeHardwareAsync(
        IReadOnlyList<string> nodes, CancellationToken cancellationToken = default) =>
        NodeFanOut.RunEachAsync(nodes, CollectHardwareAsync, _logger, cancellationToken: cancellationToken);

    public Task<IReadOnlyList<NodeResult<IReadOnlyList<NodeVolumeRow>>>> GetNodeVolumesAsync(
        IReadOnlyList<string> nodes, CancellationToken cancellationToken = default) =>
        NodeFanOut.RunEachAsync(nodes, CollectVolumesAsync, _logger, cancellationToken: cancellationToken);

    public Task<IReadOnlyList<NodeResult<IReadOnlyList<NodeNetworkAdapterRow>>>> GetNodeAdaptersAsync(
        IReadOnlyList<string> nodes, CancellationToken cancellationToken = default) =>
        NodeFanOut.RunEachAsync(nodes, CollectAdaptersAsync, _logger, cancellationToken: cancellationToken);

    private async Task<NodeHardwareRow> CollectHardwareAsync(string node, CancellationToken ct)
    {
        var cs = await _cim.QueryAsync(
            node, Ns,
            "SELECT Manufacturer, Model, TotalPhysicalMemory FROM Win32_ComputerSystem",
            CimTimeouts.Query, ct).ConfigureAwait(false);
        var os = await _cim.QueryAsync(
            node, Ns,
            "SELECT Caption, Version, LastBootUpTime, TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem",
            CimTimeouts.Query, ct).ConfigureAwait(false);
        var cpu = await _cim.QueryAsync(
            node, Ns,
            "SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor",
            CimTimeouts.Query, ct).ConfigureAwait(false);

        return SystemInfoMapper.MapHardware(node, cs.FirstOrDefault(), os.FirstOrDefault(), cpu, DateTime.Now);
    }

    private async Task<IReadOnlyList<NodeVolumeRow>> CollectVolumesAsync(string node, CancellationToken ct)
    {
        var disks = await _cim.QueryAsync(
            node, Ns,
            "SELECT DeviceID, VolumeName, FileSystem, Size, FreeSpace FROM Win32_LogicalDisk WHERE DriveType = 3",
            CimTimeouts.Query, ct).ConfigureAwait(false);
        return SystemInfoMapper.MapVolumes(node, disks);
    }

    private async Task<IReadOnlyList<NodeNetworkAdapterRow>> CollectAdaptersAsync(string node, CancellationToken ct)
    {
        var adapters = await _cim.QueryAsync(
            node, Ns,
            "SELECT Description, MACAddress, IPAddress, DefaultIPGateway, DNSServerSearchOrder, DHCPEnabled FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = True",
            CimTimeouts.Query, ct).ConfigureAwait(false);
        return SystemInfoMapper.MapAdapters(node, adapters);
    }
}
