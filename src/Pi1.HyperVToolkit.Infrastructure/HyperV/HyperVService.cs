using Microsoft.Extensions.Logging;
using Pi1.HyperVToolkit.Core.Aggregations;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Services;
using Pi1.HyperVToolkit.Infrastructure.Cim;
using Pi1.HyperVToolkit.Infrastructure.Common;
using Pi1.HyperVToolkit.Infrastructure.Mapping;

namespace Pi1.HyperVToolkit.Infrastructure.HyperV;

/// <summary>
/// Native Hyper-V inventory over root/virtualization/v2.
/// VM NICs come exclusively from the CURRENT VSSD of each VM
/// (Msvm_VirtualSystemSettingDataComponent associators): historical setting
/// objects and raw allocation objects never become adapters. Management OS
/// adapters come from Msvm_InternalEthernetPort (-ManagementOS semantics).
/// NIC inventory failures degrade to adapter-less VM rows, never to a failed node.
/// </summary>
public sealed class HyperVService : IHyperVService
{
    private const string Ns = HyperVMapper.HyperVNamespace;

    private readonly ICimQuerier _cim;
    private readonly IVhdInspector _vhd;
    private readonly ILogger<HyperVService> _logger;

    public HyperVService(ICimQuerier cim, IVhdInspector vhd, ILogger<HyperVService> logger)
    {
        _cim = cim ?? throw new ArgumentNullException(nameof(cim));
        _vhd = vhd ?? throw new ArgumentNullException(nameof(vhd));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<IReadOnlyList<NodeResult<IReadOnlyList<VirtualMachineRow>>>> GetVirtualMachinesAsync(
        IReadOnlyList<string> nodes, CancellationToken cancellationToken = default) =>
        NodeFanOut.RunEachAsync(nodes, CollectVmRowsAsync, _logger, hyperVNamespace: true, cancellationToken: cancellationToken);

    public Task<IReadOnlyList<NodeResult<IReadOnlyList<VmNetworkRow>>>> GetVmNetworkAdaptersAsync(
        IReadOnlyList<string> nodes, CancellationToken cancellationToken = default) =>
        NodeFanOut.RunEachAsync(nodes, CollectNicRowsAsync, _logger, hyperVNamespace: true, cancellationToken: cancellationToken);

    public Task<IReadOnlyList<NodeResult<IReadOnlyList<CheckpointRow>>>> GetCheckpointsAsync(
        IReadOnlyList<string> nodes, CancellationToken cancellationToken = default) =>
        NodeFanOut.RunEachAsync(nodes, CollectCheckpointsAsync, _logger, hyperVNamespace: true, cancellationToken: cancellationToken);

    public Task<IReadOnlyList<NodeResult<HostSettingsRow>>> GetHostSettingsAsync(
        IReadOnlyList<string> nodes,
        IReadOnlyDictionary<string, NodeHardwareRow?> hardwareByNode,
        CancellationToken cancellationToken = default) =>
        NodeFanOut.RunEachAsync(
            nodes,
            (node, ct) => CollectHostSettingsAsync(node, hardwareByNode, ct),
            _logger,
            hyperVNamespace: true,
            cancellationToken: cancellationToken);

    public Task<IReadOnlyList<NodeResult<IReadOnlyList<VmSwitchRow>>>> GetVirtualSwitchesAsync(
        IReadOnlyList<string> nodes, CancellationToken cancellationToken = default) =>
        NodeFanOut.RunEachAsync(nodes, CollectSwitchesAsync, _logger, hyperVNamespace: true, cancellationToken: cancellationToken);

    public Task<IReadOnlyList<NodeResult<IReadOnlyList<VmVlanRow>>>> GetVmAdapterVlansAsync(
        IReadOnlyList<string> nodes, CancellationToken cancellationToken = default) =>
        NodeFanOut.RunEachAsync(nodes, CollectVlansAsync, _logger, hyperVNamespace: true, cancellationToken: cancellationToken);

    public Task<IReadOnlyList<NodeResult<IReadOnlyList<VmStorageRow>>>> GetVmStorageAsync(
        IReadOnlyList<string> nodes,
        IReadOnlyList<CsvRow> csvRows,
        CancellationToken cancellationToken = default) =>
        NodeFanOut.RunEachAsync(
            nodes,
            (node, ct) => CollectVmStorageAsync(node, csvRows, ct),
            _logger,
            hyperVNamespace: true,
            cancellationToken: cancellationToken);

    private sealed record NicInventory(
        Dictionary<string, List<HyperVMapper.MergedNic>> VmNics,
        List<HyperVMapper.MergedNic> MgmtNics);

    private async Task<IReadOnlyList<VirtualMachineRow>> CollectVmRowsAsync(string node, CancellationToken ct)
    {
        var systems = await QuerySystemsAsync(node, ct).ConfigureAwait(false);
        var summaries = await QuerySummariesAsync(node, ct).ConfigureAwait(false);
        var processors = await _cim.QueryAsync(node, Ns, "SELECT InstanceID, ResourceType, VirtualQuantity FROM Msvm_ProcessorSettingData", CimTimeouts.Query, ct).ConfigureAwait(false);
        var memory = await _cim.QueryAsync(node, Ns, "SELECT InstanceID, ResourceType, VirtualQuantity, Reservation, Limit, DynamicMemoryEnabled FROM Msvm_MemorySettingData", CimTimeouts.Query, ct).ConfigureAwait(false);
        var inventory = await CollectNicInventoryAsync(node, systems, ct).ConfigureAwait(false);
        var guests = await QueryGuestConfigsAsync(node, ct).ConfigureAwait(false);

        var summariesByGuid = IndexByVmGuid(summaries, "Name");
        return HyperVMapper.MapVmRows(node, systems, summariesByGuid, processors, memory, inventory.VmNics, guests);
    }

    private async Task<IReadOnlyList<VmNetworkRow>> CollectNicRowsAsync(string node, CancellationToken ct)
    {
        var systems = await QuerySystemsAsync(node, ct).ConfigureAwait(false);
        var inventory = await CollectNicInventoryAsync(node, systems, ct).ConfigureAwait(false);
        var guests = await QueryGuestConfigsAsync(node, ct).ConfigureAwait(false);
        return HyperVMapper.MapNicRows(node, inventory.VmNics.Values.SelectMany(v => v), inventory.MgmtNics, guests);
    }

    /// <summary>
    /// Current NIC inventory for one node. Never throws: any NIC-stage failure
    /// degrades to fewer adapters (VMs still listed), because the collector must
    /// not lose VM rows over NIC metadata.
    /// </summary>
    private async Task<NicInventory> CollectNicInventoryAsync(
        string node,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> systems,
        CancellationToken ct)
    {
        var empty = new NicInventory(new Dictionary<string, List<HyperVMapper.MergedNic>>(StringComparer.OrdinalIgnoreCase), []);
        try
        {
            var vmGuids = systems
                .Select(s => CimValues.GetString(s, "Name").ToUpperInvariant())
                .Where(g => !string.IsNullOrEmpty(g))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var vmNames = systems.ToDictionary(
                s => CimValues.GetString(s, "Name").ToUpperInvariant(),
                s => CimValues.GetString(s, "ElementName"),
                StringComparer.OrdinalIgnoreCase);

            var switchMaps = await CollectSwitchMapsAsync(node, ct).ConfigureAwait(false);
            var currentVssdByVm = await CollectCurrentVssdMapAsync(node, vmGuids, ct).ConfigureAwait(false);
            var activeMacs = await CollectActiveMacIndexAsync(node, vmGuids, ct).ConfigureAwait(false);

            var vmNics = new Dictionary<string, List<HyperVMapper.MergedNic>>(StringComparer.OrdinalIgnoreCase);
            foreach (var vmGuid in vmGuids)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(currentVssdByVm[vmGuid]))
                {
                    // No current VSSD: the VM still appears, without adapters.
                    vmNics[vmGuid] = [];
                    continue;
                }

                var components = await _cim.QueryAsync(
                    node, Ns, AssociationQueries.ComponentsOfSetting(currentVssdByVm[vmGuid]),
                    CimTimeouts.Query, ct).ConfigureAwait(false);
                vmNics[vmGuid] = HyperVMapper.SelectCurrentVmNics(
                    vmGuid, vmNames[vmGuid], components, activeMacs,
                    switchMaps.PortToSwitch, switchMaps.GuidToSwitch).ToList();
            }

            var internalPorts = await _cim.QueryAsync(
                node, Ns,
                "SELECT InstanceID, DeviceID, ElementName, PermanentAddress, NetworkAddresses FROM Msvm_InternalEthernetPort",
                CimTimeouts.Query, ct).ConfigureAwait(false);
            var mgmtNics = HyperVMapper.SelectMgmtNics(internalPorts).ToList();

            return new NicInventory(vmNics, mgmtNics);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Zber sieťových adaptérov na uzle {Node} zlyhal; VM zostávajú bez adaptérov.", node);
            return empty;
        }
    }

    private sealed record SwitchMaps(
        Dictionary<string, string> PortToSwitch,
        Dictionary<string, string> GuidToSwitch,
        Dictionary<string, string> NameByGuid);

    private async Task<SwitchMaps> CollectSwitchMapsAsync(string node, CancellationToken ct)
    {
        var portToSwitch = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var guidToSwitch = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var nameByGuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var switches = await _cim.QueryAsync(
            node, Ns, "SELECT Name, ElementName FROM Msvm_VirtualEthernetSwitch",
            CimTimeouts.Query, ct).ConfigureAwait(false);
        foreach (var sw in switches)
        {
            ct.ThrowIfCancellationRequested();
            var switchGuid = CimValues.GetString(sw, "Name");
            var switchName = CimValues.GetString(sw, "ElementName");
            if (string.IsNullOrEmpty(switchGuid) || string.IsNullOrEmpty(switchName))
            {
                continue;
            }

            guidToSwitch.TryAdd(switchGuid.ToUpperInvariant(), switchName);
            nameByGuid.TryAdd(switchGuid.ToUpperInvariant(), switchName);

            var ports = await _cim.QueryAsync(
                node, Ns, AssociationQueries.PortsOfSwitch(switchGuid),
                CimTimeouts.Query, ct).ConfigureAwait(false);
            foreach (var port in ports)
            {
                foreach (var guid in CimValues.ExtractGuids(CimValues.GetString(port, "Name"))
                             .Concat(CimValues.ExtractGuids(CimValues.GetString(port, "DeviceID")))
                             .Concat(CimValues.ExtractGuids(CimValues.GetString(port, "InstanceID"))))
                {
                    portToSwitch.TryAdd(guid, switchName);
                }
            }
        }

        return new SwitchMaps(portToSwitch, guidToSwitch, nameByGuid);
    }

    private async Task<Dictionary<string, string>> CollectSwitchPortMapAsync(string node, CancellationToken ct) =>
        (await CollectSwitchMapsAsync(node, ct).ConfigureAwait(false)).PortToSwitch;

    private async Task<Dictionary<string, string>> CollectCurrentVssdMapAsync(
        string node, HashSet<string> vmGuids, CancellationToken ct)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var settings = await _cim.QueryAsync(
            node, Ns,
            "SELECT InstanceID, VirtualSystemIdentifier, VirtualSystemType FROM Msvm_VirtualSystemSettingData",
            CimTimeouts.Query, ct).ConfigureAwait(false);
        foreach (var setting in settings)
        {
            var systemType = CimValues.GetString(setting, "VirtualSystemType");
            if (!string.Equals(systemType, AssociationQueries.RealizedSystemType, StringComparison.OrdinalIgnoreCase) &&
                !systemType.Contains("Realized", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var vmGuid = CimValues.GetString(setting, "VirtualSystemIdentifier").ToUpperInvariant();
            if (vmGuids.Contains(vmGuid) && !map.ContainsKey(vmGuid))
            {
                map[vmGuid] = CimValues.GetString(setting, "InstanceID");
            }
        }

        foreach (var vmGuid in vmGuids)
        {
            map.TryAdd(vmGuid, string.Empty);
        }

        return map;
    }

    private async Task<Dictionary<string, string>> CollectActiveMacIndexAsync(
        string node, HashSet<string> vmGuids, CancellationToken ct)
    {
        var synthetic = await QueryActivePortsAsync(node, "Msvm_SyntheticEthernetPort", ct).ConfigureAwait(false);
        var emulated = await QueryActivePortsAsync(node, "Msvm_EmulatedEthernetPort", ct).ConfigureAwait(false);
        return HyperVMapper.BuildActiveMacIndex(synthetic.Concat(emulated).ToList(), vmGuids);
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryActivePortsAsync(
        string node, string className, CancellationToken ct)
    {
        try
        {
            return await _cim.QueryAsync(
                node, Ns,
                $"SELECT InstanceID, DeviceID, SystemName, NetworkAddresses, PermanentAddress FROM {className}",
                CimTimeouts.Query, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Active ports only backfill MACs; settings carry the primary data.
            _logger.LogDebug(ex, "{Class} nie je na uzle {Node} dostupné.", className, node);
            return [];
        }
    }

    private async Task<IReadOnlyList<CheckpointRow>> CollectCheckpointsAsync(string node, CancellationToken ct)
    {
        var systems = await QuerySystemsAsync(node, ct).ConfigureAwait(false);
        var namesByGuid = systems.ToDictionary(
            s => CimValues.GetString(s, "Name").ToUpperInvariant(),
            s => CimValues.GetString(s, "ElementName"),
            StringComparer.OrdinalIgnoreCase);
        var snapshots = await _cim.QueryAsync(
            node, Ns,
            "SELECT ElementName, VirtualSystemType, VirtualSystemSubType, VirtualSystemIdentifier, Parent, InstanceID, CreationTime FROM Msvm_VirtualSystemSettingData",
            CimTimeouts.Query, ct).ConfigureAwait(false);
        return HyperVMapper.MapCheckpoints(node, snapshots, namesByGuid);
    }

    private async Task<HostSettingsRow> CollectHostSettingsAsync(
        string node, IReadOnlyDictionary<string, NodeHardwareRow?> hardwareByNode, CancellationToken ct)
    {
        var management = await _cim.QueryAsync(
            node, Ns,
            "SELECT NumaSpanningEnabled, EnhancedSessionModeEnabled, DefaultExternalDataRoot, DefaultVirtualHardDiskPath FROM Msvm_VirtualSystemManagementServiceSettingData",
            CimTimeouts.Query, ct).ConfigureAwait(false);
        var migration = await _cim.QueryAsync(
            node, Ns,
            "SELECT EnableVirtualSystemMigration, MaximumActiveVirtualSystemMigration FROM Msvm_VirtualSystemMigrationServiceSettingData",
            CimTimeouts.Query, ct).ConfigureAwait(false);

        if (management.Count == 0 && migration.Count == 0)
        {
            throw new InvalidOperationException(
                "Hyper-V WMI (root\\virtualization\\v2) na uzle neodpovedá. (0x8004100E Hyper-V)");
        }

        hardwareByNode.TryGetValue(node, out var hardware);
        return HyperVMapper.MapHostSettings(
            node,
            management.FirstOrDefault(),
            migration.FirstOrDefault(),
            hardware);
    }

    private Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QuerySystemsAsync(string node, CancellationToken ct) =>
        _cim.QueryAsync(
            node, Ns,
            "SELECT Name, ElementName, EnabledState, OnTimeInMilliseconds FROM Msvm_ComputerSystem WHERE Caption = 'Virtual Machine'",
            CimTimeouts.Query, ct);

    private async Task<IReadOnlyList<VmSwitchRow>> CollectSwitchesAsync(string node, CancellationToken ct)
    {
        var switches = await _cim.QueryAsync(
            node, Ns, "SELECT Name, ElementName FROM Msvm_VirtualEthernetSwitch",
            CimTimeouts.Query, ct).ConfigureAwait(false);
        var rows = new List<VmSwitchRow>();
        // Management-OS attachment evidence, collected ONCE per node (never a
        // per-switch ASSOCIATORS-only test: direct InternalEthernetPort→switch
        // association provably returns nothing on real hosts).
        var hostVnicBySwitch = await QueryHostVnicAllocationsAsync(node, ct).ConfigureAwait(false);
        var internalPorts = await QueryAllInternalPortsAsync(node, ct).ConfigureAwait(false);
        foreach (var sw in switches)
        {
            ct.ThrowIfCancellationRequested();
            var guid = CimValues.GetString(sw, "Name");
            var name = CimValues.GetString(sw, "ElementName");
            if (string.IsNullOrEmpty(guid) || string.IsNullOrEmpty(name))
            {
                continue;
            }

            var external = await QuerySwitchPortsAsync(node, guid, external: true, ct).ConfigureAwait(false);
            var hasExternal = external.Count > 0;
            var hasInternal = HasHostVnicAllocation(hostVnicBySwitch, guid)
                || InternalPortCorrelates(internalPorts, guid, name);
            rows.Add(HyperVMapper.MapSwitchRow(
                node, name,
                hasExternal, hasInternal,
                DescribePhysicalAdapter(node, external, ct)));
        }

        return rows
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Active host-vNIC allocations indexed by switch GUID: an allocation
    /// whose InstanceID embeds the switch GUID as its FIRST GUID, whose
    /// HostResource references the host computer system, and whose
    /// EnabledState is active (2, or absent). Live shape: ElementName
    /// "Host Vnic &lt;switchGuid&gt;", InstanceID
    /// "Microsoft:&lt;switchGuid&gt;\&lt;portGuid&gt;".
    /// </summary>
    internal static Dictionary<string, bool> IndexHostVnicAllocations(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> allocations)
    {
        var index = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var allocation in allocations)
        {
            // HostResource is a CIM string array; any entry may carry the host
            // computer-system reference.
            var hostAttached = CimValues.GetStringArray(allocation, "HostResource")
                .Any(h => h.Contains("Msvm_ComputerSystem", StringComparison.OrdinalIgnoreCase));
            if (!hostAttached)
            {
                continue;
            }

            var guids = CimValues.ExtractGuids(CimValues.GetString(allocation, "InstanceID"));
            if (guids.Count == 0)
            {
                continue;
            }

            var active = HyperVMapper.IsAllocationActive(allocation);
            index.TryAdd(guids[0], active);
            if (active)
            {
                index[guids[0]] = true;
            }
        }

        return index;
    }

    private static bool HasHostVnicAllocation(
        Dictionary<string, bool> hostVnicBySwitch, string switchGuid) =>
        hostVnicBySwitch.TryGetValue(switchGuid, out var active) && active;

    /// <summary>
    /// Internal-port correlation fallback: an Msvm_InternalEthernetPort whose
    /// identity embeds the switch GUID, or whose ElementName equals the switch
    /// name (internal ports are named after their switch). Weaker than the
    /// host-vNIC allocation proof above, but valid corroboration.
    /// </summary>
    internal static bool InternalPortCorrelates(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> internalPorts,
        string switchGuid,
        string switchName)
    {
        foreach (var port in internalPorts)
        {
            var guids = CimValues.ExtractGuids(CimValues.GetString(port, "InstanceID"))
                .Concat(CimValues.ExtractGuids(CimValues.GetString(port, "DeviceID")))
                .Concat(CimValues.ExtractGuids(CimValues.GetString(port, "Name")));
            if (guids.Contains(switchGuid, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(switchName) &&
                string.Equals(CimValues.GetString(port, "ElementName"), switchName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<Dictionary<string, bool>> QueryHostVnicAllocationsAsync(string node, CancellationToken ct)
    {
        try
        {
            var allocations = await _cim.QueryAsync(
                node, Ns,
                "SELECT ElementName, InstanceID, HostResource, EnabledState FROM Msvm_EthernetPortAllocationSettingData",
                CimTimeouts.Query, ct).ConfigureAwait(false);
            return IndexHostVnicAllocations(allocations);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Host vNIC alokácie nie sú na uzle {Node} dostupné.", node);
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAllInternalPortsAsync(
        string node, CancellationToken ct)
    {
        try
        {
            return await _cim.QueryAsync(
                node, Ns,
                "SELECT InstanceID, DeviceID, Name, ElementName FROM Msvm_InternalEthernetPort",
                CimTimeouts.Query, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Interné porty nie sú na uzle {Node} dostupné.", node);
            return [];
        }
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QuerySwitchPortsAsync(
        string node, string switchGuid, bool external, CancellationToken ct)
    {
        try
        {
            var wql = external
                ? AssociationQueries.ExternalPortsOfSwitch(switchGuid)
                : AssociationQueries.InternalPortsOfSwitch(switchGuid);
            return await _cim.QueryAsync(node, Ns, wql, CimTimeouts.Query, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Port association failure degrades one switch to fewer facts
            // (possibly Private/Unknown), never the whole node.
            _logger.LogDebug(ex, "Porty prepínača na uzle {Node} sa nedajú načítať.", node);
            return [];
        }
    }

    private string DescribePhysicalAdapter(
        string node,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> externalPorts,
        CancellationToken ct)
    {
        // Best-effort Get-VMSwitch NetAdapterInterfaceDescription parity: the
        // external uplink's ElementName. A Win32_NetworkAdapter join was
        // considered, but DeviceID correlation is unverified — this stays
        // minimal and honest (PARTIAL until elevated switch parity).
        _ = node;
        _ = ct;
        foreach (var port in externalPorts)
        {
            var name = CimValues.GetString(port, "ElementName");
            if (!string.IsNullOrEmpty(name))
            {
                return name;
            }
        }

        return string.Empty;
    }

    private async Task<IReadOnlyList<VmVlanRow>> CollectVlansAsync(string node, CancellationToken ct)
    {
        var systems = await QuerySystemsAsync(node, ct).ConfigureAwait(false);
        var vmGuids = systems
            .Select(s => CimValues.GetString(s, "Name").ToUpperInvariant())
            .Where(g => !string.IsNullOrEmpty(g))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var vmNames = systems.ToDictionary(
            s => CimValues.GetString(s, "Name").ToUpperInvariant(),
            s => CimValues.GetString(s, "ElementName"),
            StringComparer.OrdinalIgnoreCase);
        var currentVssdByVm = await CollectCurrentVssdMapAsync(node, vmGuids, ct).ConfigureAwait(false);

        var vlanSettings = await QueryVlanSettingsAsync(node, ct).ConfigureAwait(false);
        var vlanByPort = HyperVMapper.IndexVlanSettingsByPort(vlanSettings);

        var rows = new List<VmVlanRow>();
        foreach (var vmGuid in vmGuids)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(currentVssdByVm[vmGuid]))
            {
                continue;
            }

            var components = await _cim.QueryAsync(
                node, Ns, AssociationQueries.ComponentsOfSetting(currentVssdByVm[vmGuid]),
                CimTimeouts.Query, ct).ConfigureAwait(false);
            foreach (var component in components)
            {
                var className = CimValues.GetString(component, "__Class");
                if (!AssociationQueries.EthernetSettingClasses.Contains(className))
                {
                    continue;
                }

                var adapterName = CimValues.GetString(component, "ElementName");
                if (string.IsNullOrEmpty(adapterName))
                {
                    adapterName = "Network Adapter";
                }

                var emittedForPort = false;
                foreach (var portGuid in HyperVMapper.GetConnectionPortGuids(component))
                {
                    if (vlanByPort.TryGetValue(portGuid, out var setting))
                    {
                        rows.Add(HyperVMapper.MapVlanRow(node, vmNames[vmGuid], adapterName, setting));
                        emittedForPort = true;
                    }
                }

                if (!emittedForPort)
                {
                    // No explicit setting: Get-VMNetworkAdapterVlan reports Untagged.
                    rows.Add(new VmVlanRow(node, vmNames[vmGuid], adapterName, "Untagged", 0, 0, string.Empty));
                }
            }
        }

        return rows
            .OrderBy(r => r.VMName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.VMNetworkAdapterName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryVlanSettingsAsync(
        string node, CancellationToken ct)
    {
        try
        {
            return await _cim.QueryAsync(
                node, Ns,
                "SELECT InstanceID, OperationMode, AccessVlanId, NativeVlanId, TrunkVlanIdArray FROM Msvm_EthernetSwitchPortVlanSettingData",
                CimTimeouts.Query, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "VLAN nastavenia nie sú na uzle {Node} dostupné.", node);
            return [];
        }
    }

    private async Task<IReadOnlyList<VmStorageRow>> CollectVmStorageAsync(
        string node, IReadOnlyList<CsvRow> csvRows, CancellationToken ct)
    {
        var systems = await QuerySystemsAsync(node, ct).ConfigureAwait(false);
        var vmGuids = systems
            .Select(s => CimValues.GetString(s, "Name").ToUpperInvariant())
            .Where(g => !string.IsNullOrEmpty(g))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var vmByGuid = systems.ToDictionary(
            s => CimValues.GetString(s, "Name").ToUpperInvariant(),
            s => (Name: CimValues.GetString(s, "ElementName"),
                  State: HyperVMapper.MapVmState(s.TryGetValue("EnabledState", out var es) ? es : null)),
            StringComparer.OrdinalIgnoreCase);
        var currentVssdByVm = await CollectCurrentVssdMapAsync(node, vmGuids, ct).ConfigureAwait(false);

        // Bounded parallelism for the N Get-VHD-equivalent inspections:
        // one slow/missing VHD never blocks unrelated disks.
        using var gate = new SemaphoreSlim(4);
        var diskTasks = new List<Task<VmStorageRow?>>();
        foreach (var vmGuid in vmGuids)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(currentVssdByVm[vmGuid]))
            {
                continue;
            }

            var components = await _cim.QueryAsync(
                node, Ns, AssociationQueries.ComponentsOfSetting(currentVssdByVm[vmGuid]),
                CimTimeouts.Query, ct).ConfigureAwait(false);
            var vmInfo = vmByGuid[vmGuid];
            foreach (var drive in HyperVMapper.SelectCurrentVmDrives(components))
            {
                diskTasks.Add(InspectDiskAsync(node, vmInfo.Name, vmInfo.State, drive, csvRows, gate, ct));
            }
        }

        var diskRows = await Task.WhenAll(diskTasks).ConfigureAwait(false);
        return diskRows
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();
    }

    private async Task<VmStorageRow?> InspectDiskAsync(
        string node,
        string vmName,
        string vmState,
        HyperVMapper.VmDriveInfo drive,
        IReadOnlyList<CsvRow> csvRows,
        SemaphoreSlim gate,
        CancellationToken outerCt)
    {
        await gate.WaitAsync(outerCt).ConfigureAwait(false);
        try
        {
            var metadata = await _vhd.InspectAsync(node, drive.Path, outerCt).ConfigureAwait(false);
            var csv = StorageAggregations.FindBestCsvMatch(drive.Path, csvRows);
            return new VmStorageRow(
                node,
                vmName,
                vmState,
                drive.Controller,
                metadata is null ? "Unknown" : metadata.VhdFormat,
                metadata is null ? "Unknown" : metadata.VhdType,
                metadata is null ? null : CimValues.BytesToGigabytes(metadata.SizeBytes),
                metadata?.FileSizeBytes is null ? null : CimValues.BytesToGigabytes(metadata.FileSizeBytes.Value),
                csv is null ? string.Empty : csv.Name,
                csv?.FreeGB,
                csv?.FreePercent,
                drive.Path,
                metadata is null ? null : (long?)metadata.SizeBytes,
                metadata?.FileSizeBytes is null ? null : (long?)metadata.FileSizeBytes.Value,
                csv?.FreeBytes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Disk {Path} VM {VM} na uzle {Node} sa nepodarilo spracovať.", drive.Path, vmName, node);
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QuerySummariesAsync(string node, CancellationToken ct)
    {
        try
        {
            return await _cim.QueryAsync(
                node, Ns,
                "SELECT Name, NumberOfProcessors, MemoryUsage, AvailableMemoryBuffer, UpTime FROM Msvm_SummaryInformation",
                CimTimeouts.Query, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Summary information is an optimization (counts, memory, uptime);
            // the collector stays functional without it.
            _logger.LogDebug(ex, "Msvm_SummaryInformation nie je na uzle {Node} dostupné.", node);
            return [];
        }
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryGuestConfigsAsync(string node, CancellationToken ct)
    {
        try
        {
            return await _cim.QueryAsync(
                node, Ns,
                "SELECT InstanceID, IPAddresses FROM Msvm_GuestNetworkAdapterConfiguration",
                CimTimeouts.Query, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Guest IPs depend on integration services; absence only empties IP fields.
            _logger.LogDebug(ex, "Msvm_GuestNetworkAdapterConfiguration nie je na uzle {Node} dostupné.", node);
            return [];
        }
    }

    private static Dictionary<string, IReadOnlyDictionary<string, object?>> IndexByVmGuid(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> items, string keyProperty)
    {
        var index = new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var key = CimValues.GetString(item, keyProperty).ToUpperInvariant();
            if (!string.IsNullOrEmpty(key))
            {
                index[key] = item;
            }
        }

        return index;
    }
}
