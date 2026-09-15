using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Utilities;
using Pi1.HyperVToolkit.Infrastructure.HyperV;

namespace Pi1.HyperVToolkit.Infrastructure.Mapping;

/// <summary>
/// Pure Hyper-V WMI (root/virtualization/v2) to row-model mapping.
/// Every method takes plain property bags, so all parity rules are unit-testable
/// without Hyper-V. Documented mapping assumptions (verified against the live
/// WMI schema on Server/Win10; value-level parity needs a VM-bearing host):
///
/// - VMs: Msvm_ComputerSystem WHERE Caption='Virtual Machine' (Name=GUID).
/// - vCPU: Msvm_ProcessorSettingData.VirtualQuantity, joined by VM GUID embedded
///   in InstanceID (ResourceType 3 guard with contains-fallback).
/// - Memory config: Msvm_MemorySettingData (VirtualQuantity=startup MB,
///   Reservation=min MB, Limit=max MB, DynamicMemoryEnabled).
/// - Assigned: Msvm_SummaryInformation.MemoryUsage (MB). Demand: MemoryUsage
///   minus AvailableMemoryBuffer when both are present, otherwise Assigned.
///   Off VMs report 0/0 (Get-VM parity). Waste is rounded from the MB
///   difference exactly like PowerShell rounds the byte difference.
/// - Uptime: Msvm_ComputerSystem.OnTimeInMilliseconds, fallback
///   Msvm_SummaryInformation.UpTime (seconds), else null ("-").
/// - NICs: CURRENT configuration only, selected through authoritative WMI
///   relationships (VM classes below). Raw allocation objects are never
///   standalone adapters. MAC: setting-data Address (StaticMacAddress is a
///   BOOLEAN flag, never a MAC source), else the active port. Switch: the NIC
///   setting's live Connection[] resolved through the switch-port map, else
///   empty — a disconnected adapter MUST stay SwitchName="". Management OS
///   adapters come from Msvm_InternalEthernetPort (ElementName + PermanentAddress).
///   Guest IPs stay separate runtime data — absence never hides MAC/switch.
/// </summary>
public static class HyperVMapper
{
    public const string HyperVNamespace = @"root\virtualization\v2";

    public static string MapVmState(object? enabledState)
    {
        if (enabledState is null)
        {
            return "Unknown";
        }

        long value;
        try
        {
            value = Convert.ToInt64(enabledState, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception) when (enabledState is string || enabledState is IConvertible)
        {
            return "Unknown";
        }

        return value switch
        {
            2 => "Running",
            3 => "Off",
            4 => "Stopping",
            10 => "Starting",
            32768 => "Paused",
            32769 => "Saved",
            _ => $"Unknown ({value})",
        };
    }

    public static IReadOnlyList<VirtualMachineRow> MapVmRows(
        string node,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> systems,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> summariesByGuid,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> processorSettings,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> memorySettings,
        IReadOnlyDictionary<string, List<MergedNic>> vmNicsByGuid,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> guestConfigs)
    {
        var rows = new List<VirtualMachineRow>();
        foreach (var system in systems)
        {
            var guid = CimValues.GetString(system, "Name").ToUpperInvariant();
            var name = CimValues.GetString(system, "ElementName");
            if (string.IsNullOrEmpty(guid) || string.IsNullOrEmpty(name))
            {
                continue;
            }

            var state = MapVmState(system.TryGetValue("EnabledState", out var es) ? es : null);
            var isRunning = string.Equals(state, "Running", StringComparison.Ordinal);

            var cpu = 0;
            var proc = FindSettingForVm(processorSettings, guid, resourceType: 3);
            if (proc is not null)
            {
                cpu = CimValues.GetInt(proc, "VirtualQuantity") ?? 0;
            }
            else if (summariesByGuid.TryGetValue(guid, out var summaryCpu))
            {
                cpu = CimValues.GetInt(summaryCpu, "NumberOfProcessors") ?? 0;
            }

            var mem = FindSettingForVm(memorySettings, guid, resourceType: 4);
            var startupMb = mem is not null ? CimValues.GetDouble(mem, "VirtualQuantity") ?? 0 : 0;
            var minMb = mem is not null ? CimValues.GetDouble(mem, "Reservation") ?? 0 : 0;
            var maxMb = mem is not null ? CimValues.GetDouble(mem, "Limit") ?? 0 : 0;
            var dynamic = mem is not null && (CimValues.GetBool(mem, "DynamicMemoryEnabled") ?? false);

            double assignedMb = 0;
            double demandMb = 0;
            if (isRunning && summariesByGuid.TryGetValue(guid, out var summary))
            {
                var usageMb = CimValues.GetDouble(summary, "MemoryUsage");
                var bufferMb = CimValues.GetDouble(summary, "AvailableMemoryBuffer");
                if (usageMb.HasValue)
                {
                    assignedMb = usageMb.Value;
                    demandMb = bufferMb.HasValue ? Math.Max(usageMb.Value - bufferMb.Value, 0) : usageMb.Value;
                }
            }

            var assignedGb = CimValues.MegabytesToGigabytes(assignedMb);
            var demandGb = CimValues.MegabytesToGigabytes(demandMb);
            var wasteGb = CimValues.Round1((assignedMb - demandMb) / 1024.0);

            // Raw display precision (bytes, exact from MB integers).
            var assignedBytes = CimValues.MegabytesToBytes(assignedMb);
            var demandBytes = CimValues.MegabytesToBytes(demandMb);
            var wasteBytes = assignedBytes - demandBytes;
            var startupBytes = CimValues.MegabytesToBytes(startupMb);
            var minBytes = CimValues.MegabytesToBytes(minMb);
            var maxBytes = CimValues.MegabytesToBytes(maxMb);

            TimeSpan? uptime = null;
            var onTimeMs = CimValues.GetDouble(system, "OnTimeInMilliseconds");
            if (onTimeMs is > 0)
            {
                uptime = TimeSpan.FromMilliseconds(onTimeMs.Value);
            }
            else if (isRunning && summariesByGuid.TryGetValue(guid, out var summaryUp))
            {
                var upSeconds = CimValues.GetDouble(summaryUp, "UpTime");
                if (upSeconds is > 0)
                {
                    uptime = TimeSpan.FromSeconds(upSeconds.Value);
                }
            }

            var nics = vmNicsByGuid.TryGetValue(guid, out var list) ? list : new List<MergedNic>();
            if (nics.Count == 0)
            {
                rows.Add(new VirtualMachineRow(node, name, state, cpu, assignedGb, demandGb, wasteGb,
                    string.Empty, string.Empty, string.Empty, uptime, dynamic,
                    CimValues.MegabytesToGigabytes(startupMb),
                    CimValues.MegabytesToGigabytes(minMb),
                    CimValues.MegabytesToGigabytes(maxMb),
                    assignedBytes, demandBytes, wasteBytes, startupBytes, minBytes, maxBytes));
            }
            else
            {
                foreach (var nic in nics)
                {
                    var ips = FindGuestIps(nic.AdapterGuids, guid, guestConfigs);
                    rows.Add(new VirtualMachineRow(node, name, state, cpu, assignedGb, demandGb, wasteGb,
                        NetworkText.FilterIPv4(ips), nic.Mac, nic.Switch, uptime, dynamic,
                        CimValues.MegabytesToGigabytes(startupMb),
                        CimValues.MegabytesToGigabytes(minMb),
                        CimValues.MegabytesToGigabytes(maxMb),
                        assignedBytes, demandBytes, wasteBytes, startupBytes, minBytes, maxBytes));
                }
            }
        }

        return rows;
    }

    /// <summary>
    /// Get-VMNetworkAdapter -All parity: one row per current VM adapter plus one
    /// row per Management OS adapter (VMName empty). Raw allocation objects never
    /// become rows. Presentation filtering is a separate, later concern.
    /// </summary>
    public static IReadOnlyList<VmNetworkRow> MapNicRows(
        string node,
        IEnumerable<MergedNic> vmNics,
        IReadOnlyList<MergedNic> mgmtNics,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> guestConfigs)
    {
        var rows = new List<VmNetworkRow>();
        foreach (var nic in vmNics.Concat(mgmtNics))
        {
            var ips = FindGuestIps(nic.AdapterGuids, nic.VmGuid, guestConfigs);
            var allIps = string.Join(", ", ips.Where(s => !string.IsNullOrWhiteSpace(s)));
            rows.Add(new VmNetworkRow(
                node,
                nic.VmName,
                NetworkText.FilterIPv4(ips),
                nic.Mac,
                nic.Switch,
                allIps));
        }

        return rows;
    }

    public static HostSettingsRow MapHostSettings(
        string node,
        IReadOnlyDictionary<string, object?>? managementSetting,
        IReadOnlyDictionary<string, object?>? migrationSetting,
        NodeHardwareRow? hardware)
    {
        return new HostSettingsRow(
            node,
            hardware?.LogicalCPU,
            hardware?.RAMGB,
            hardware?.FreeRAMGB,
            hardware?.RAMUsedPct,
            managementSetting is null ? null : CimValues.GetBool(managementSetting, "NumaSpanningEnabled"),
            migrationSetting is null ? null : CimValues.GetBool(migrationSetting, "EnableVirtualSystemMigration"),
            migrationSetting is null ? null : CimValues.GetInt(migrationSetting, "MaximumActiveVirtualSystemMigration"),
            managementSetting is null ? null : CimValues.GetBool(managementSetting, "EnhancedSessionModeEnabled"),
            managementSetting is null ? string.Empty : CimValues.GetString(managementSetting, "DefaultExternalDataRoot"),
            managementSetting is null ? string.Empty : CimValues.GetString(managementSetting, "DefaultVirtualHardDiskPath"),
            hardware?.TotalBytes,
            hardware?.FreeBytes);
    }

    public static IReadOnlyList<CheckpointRow> MapCheckpoints(
        string node,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> settingData,
        IReadOnlyDictionary<string, string> vmNameByGuid)
    {
        var rows = new List<CheckpointRow>();
        foreach (var item in settingData)
        {
            var systemType = CimValues.GetString(item, "VirtualSystemType");
            if (!systemType.Contains("Snapshot", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = CimValues.GetString(item, "ElementName");
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var vmName = "(nezistené)";
            var candidates = CimValues.ExtractGuids(CimValues.GetString(item, "Parent"))
                .Concat(CimValues.ExtractGuids(CimValues.GetString(item, "VirtualSystemIdentifier")))
                .Concat(CimValues.ExtractGuids(CimValues.GetString(item, "InstanceID")));
            foreach (var guid in candidates)
            {
                if (vmNameByGuid.TryGetValue(guid, out var found))
                {
                    vmName = found;
                    break;
                }
            }

            rows.Add(new CheckpointRow(
                node,
                vmName,
                name,
                CimValues.GetDateTime(item, "CreationTime"),
                MapSnapshotType(systemType, item)));
        }

        return rows;
    }

    internal static string MapSnapshotType(string systemType, IReadOnlyDictionary<string, object?> item)
    {
        if (systemType.Contains("Recovery", StringComparison.OrdinalIgnoreCase))
        {
            return "Recovery";
        }

        if (systemType.Contains("Planned", StringComparison.OrdinalIgnoreCase))
        {
            return "Planned";
        }

        if (systemType.Contains("Full", StringComparison.OrdinalIgnoreCase) ||
            systemType.Contains("Standard", StringComparison.OrdinalIgnoreCase))
        {
            return "Standard";
        }

        var subType = CimValues.GetString(item, "VirtualSystemSubType");
        return string.IsNullOrEmpty(subType) ? "Snapshot" : subType;
    }

    private static IReadOnlyDictionary<string, object?>? FindSettingForVm(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> settings, string vmGuid, int resourceType)
    {
        IReadOnlyDictionary<string, object?>? fallback = null;
        foreach (var setting in settings)
        {
            var instanceId = CimValues.GetString(setting, "InstanceID");
            if (!instanceId.Contains(vmGuid, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var type = CimValues.GetInt(setting, "ResourceType");
            if (type == resourceType)
            {
                return setting;
            }

            fallback ??= setting;
        }

        return fallback;
    }

    /// <summary>
    /// One real network adapter: either a VM's CURRENT NIC (VmGuid set) or a
    /// Management OS adapter (VmGuid null → blank VMName). Raw allocation or
    /// historical setting objects never become MergedNic instances.
    /// </summary>
    public sealed record MergedNic(
        string? VmGuid,
        string VmName,
        string AdapterKey,
        IReadOnlyList<string> AdapterGuids,
        string AdapterName,
        string Mac,
        string Switch);

    /// <summary>
    /// Selects a VM's CURRENT NICs from the components of its CURRENT VSSD.
    /// Historical/inactive setting objects (same AdapterId under another VSSD)
    /// never reach this method — the caller queries components per current VSSD.
    /// Both synthetic and emulated setting classes are honored; genuinely
    /// emulated adapters keep working, but nothing here assumes DOS is emulated
    /// (DOS 6.22 is synthetic, IsLegacy=False).
    ///
    /// Switch resolution (corrected per live forensics): the CURRENT
    /// Msvm_EthernetPortAllocationSettingData child of the NIC setting is
    /// authoritative — an ACTIVE allocation whose HostResource references
    /// Msvm_VirtualEthernetSwitch resolves that switch GUID to its ElementName.
    /// Connection[] is corroborating fallback only; a disabled allocation (or a
    /// disconnected NIC with neither) yields SwitchName="". Historical-VSSD
    /// allocations never participate: only components of the SAME current VSSD
    /// are inspected here.
    /// </summary>
    public static IReadOnlyList<MergedNic> SelectCurrentVmNics(
        string vmGuid,
        string vmName,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> vssdComponents,
        IReadOnlyDictionary<string, string> activeMacByAdapterGuid,
        IReadOnlyDictionary<string, string> switchNameByPortGuid) =>
        SelectCurrentVmNics(
            vmGuid, vmName, vssdComponents, activeMacByAdapterGuid,
            switchNameByPortGuid,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    public static IReadOnlyList<MergedNic> SelectCurrentVmNics(
        string vmGuid,
        string vmName,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> vssdComponents,
        IReadOnlyDictionary<string, string> activeMacByAdapterGuid,
        IReadOnlyDictionary<string, string> switchNameByPortGuid,
        IReadOnlyDictionary<string, string> switchNameByGuid)
    {
        var result = new List<MergedNic>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var component in vssdComponents)
        {
            var className = CimValues.GetString(component, "__Class");
            if (!AssociationQueries.EthernetSettingClasses.Contains(className))
            {
                continue;
            }

            var adapterGuids = CimValues.ExtractGuids(CimValues.GetString(component, "InstanceID"))
                .Where(g => !string.Equals(g, vmGuid, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // NOTE: StaticMacAddress is a BOOLEAN flag — it is never a MAC source.
            // Reading it as a string would manufacture MacAddress="False" rows.
            var mac = CimValues.GetString(component, "Address");
            if (string.IsNullOrEmpty(mac))
            {
                foreach (var guid in adapterGuids)
                {
                    if (activeMacByAdapterGuid.TryGetValue(guid, out var activeMac))
                    {
                        mac = activeMac;
                        break;
                    }
                }
            }

            var key = adapterGuids.FirstOrDefault()
                ?? (string.IsNullOrEmpty(mac) ? null : "MAC:" + NetworkText.NormalizeMac(mac).ToUpperInvariant());
            if (key is null || !seenKeys.Add(key))
            {
                continue;
            }

            result.Add(new MergedNic(
                vmGuid,
                vmName,
                key,
                adapterGuids,
                CimValues.GetString(component, "ElementName"),
                mac,
                SwitchFromAllocationOrConnection(
                    component, adapterGuids, vssdComponents, switchNameByPortGuid, switchNameByGuid)));
        }

        return result;
    }

    /// <summary>
    /// Current-VSSD allocation-first switch resolution. Finds the
    /// Msvm_EthernetPortAllocationSettingData in the SAME component set whose
    /// Parent references this NIC setting: active + switch HostResource wins;
    /// disabled forces "". Only when NO current allocation exists does the
    /// legacy Connection[] port map apply as corroboration.
    /// </summary>
    internal static string SwitchFromAllocationOrConnection(
        IReadOnlyDictionary<string, object?> nicSetting,
        IReadOnlyList<string> adapterGuids,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> vssdComponents,
        IReadOnlyDictionary<string, string> switchNameByPortGuid,
        IReadOnlyDictionary<string, string> switchNameByGuid)
    {
        var nicInstanceId = CimValues.GetString(nicSetting, "InstanceID");
        var allocationFound = false;
        foreach (var component in vssdComponents)
        {
            if (!string.Equals(CimValues.GetString(component, "__Class"),
                    "Msvm_EthernetPortAllocationSettingData", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!AllocationBelongsToNic(component, nicInstanceId, adapterGuids))
            {
                continue;
            }

            allocationFound = true;
            if (!IsAllocationActive(component))
            {
                // A disabled current allocation is proof of disconnection.
                return string.Empty;
            }

            // Active allocation: resolve through the referenced switch (or,
            // rarely, through the referenced switch port). HostResource is a
            // CIM string array — every entry is inspected. An unresolvable
            // GUID reports disconnected rather than guessing another switch.
            foreach (var hostResource in CimValues.GetStringArray(component, "HostResource"))
            {
                foreach (var guid in CimValues.ExtractGuids(hostResource))
                {
                    if (switchNameByGuid.TryGetValue(guid, out var switchName) ||
                        switchNameByPortGuid.TryGetValue(guid, out switchName))
                    {
                        return switchName;
                    }
                }
            }
        }

        if (allocationFound)
        {
            return string.Empty;
        }

        return SwitchFromConnection(nicSetting, switchNameByPortGuid);
    }

    private static bool AllocationBelongsToNic(
        IReadOnlyDictionary<string, object?> allocation,
        string nicInstanceId,
        IReadOnlyList<string> adapterGuids)
    {
        allocation.TryGetValue("Parent", out var parentRaw);
        var parent = ReferenceIds.TryGetReferencedInstanceId(parentRaw) ?? string.Empty;
        if (string.IsNullOrEmpty(parent))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(nicInstanceId) &&
            string.Equals(parent, nicInstanceId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Shortened Parent references still count — but ONLY within the same
        // VSSD: the parent must share the NIC InstanceID's leading (VSSD)
        // GUID as well as an adapter GUID. This blocks historical-VSSD
        // allocations (same AdapterId, foreign VSSD) from merging in.
        var nicGuids = CimValues.ExtractGuids(nicInstanceId);
        if (nicGuids.Count == 0)
        {
            return false;
        }

        var parentGuids = CimValues.ExtractGuids(parent);
        return parentGuids.Contains(nicGuids[0], StringComparer.OrdinalIgnoreCase)
            && parentGuids.Any(g =>
                adapterGuids.Contains(g, StringComparer.OrdinalIgnoreCase) ||
                nicGuids.Contains(g, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Allocation EnabledState 2 (active/enabled) — or absent (older providers
    /// omit it) — counts as active. Any other present state is disabled.
    /// </summary>
    public static bool IsAllocationActive(IReadOnlyDictionary<string, object?> allocation)
    {
        if (!allocation.TryGetValue("EnabledState", out var value) || value is null)
        {
            return true;
        }

        try
        {
            return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture) == 2;
        }
        catch (Exception) when (value is string || value is IConvertible)
        {
            return false;
        }
    }

    /// <summary>
    /// Management OS adapters from Msvm_InternalEthernetPort (Get-VMNetworkAdapter
    /// -ManagementOS semantics): MAC from PermanentAddress (else NetworkAddresses),
    /// switch/name from ElementName (internal ports are named after their switch:
    /// "WSL (Hyper-V firewall)", "Default Switch"). Ports without any address are
    /// skipped: reportable adapters always carry a MAC (every -All row has one).
    /// </summary>
    public static IReadOnlyList<MergedNic> SelectMgmtNics(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> internalPorts)
    {
        var result = new List<MergedNic>();
        foreach (var port in internalPorts)
        {
            var mac = FirstNonEmpty(
                CimValues.GetString(port, "PermanentAddress"),
                CimValues.GetStringArray(port, "NetworkAddresses").FirstOrDefault());
            if (string.IsNullOrEmpty(mac))
            {
                continue;
            }

            var name = CimValues.GetString(port, "ElementName");
            var adapterGuids = CimValues.ExtractGuids(CimValues.GetString(port, "InstanceID"))
                .Concat(CimValues.ExtractGuids(CimValues.GetString(port, "DeviceID")))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var key = adapterGuids.FirstOrDefault() ?? ("MAC:" + NetworkText.NormalizeMac(mac).ToUpperInvariant());
            result.Add(new MergedNic(null, string.Empty, key, adapterGuids, name, mac, name));
        }

        return result;
    }

    /// <summary>
    /// Indexes active (synthetic/emulated) ports by every non-VM GUID they embed,
    /// so current settings with an empty Address can backfill their MAC.
    /// </summary>
    public static Dictionary<string, string> BuildActiveMacIndex(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> activePorts,
        IReadOnlyCollection<string> vmGuids)
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var port in activePorts)
        {
            var mac = FirstNonEmpty(
                CimValues.GetStringArray(port, "NetworkAddresses").FirstOrDefault(),
                CimValues.GetString(port, "PermanentAddress"));
            if (string.IsNullOrEmpty(mac))
            {
                continue;
            }

            var guids = CimValues.ExtractGuids(CimValues.GetString(port, "InstanceID"))
                .Concat(CimValues.ExtractGuids(CimValues.GetString(port, "DeviceID")))
                .Where(g => !vmGuids.Contains(g));
            foreach (var guid in guids)
            {
                index.TryAdd(guid, mac);
            }
        }

        return index;
    }

    /// <summary>
    /// Live attachment only: the setting's Connection[] references switch ports;
    /// an empty Connection[] means disconnected and yields SwitchName="".
    /// Disabled allocation objects, HostResource and single-switch guesses are
    /// never consulted — a disconnected adapter must stay disconnected.
    /// </summary>
    internal static string SwitchFromConnection(
        IReadOnlyDictionary<string, object?> nicSetting,
        IReadOnlyDictionary<string, string> switchNameByPortGuid)
    {
        foreach (var connection in CimValues.GetStringArray(nicSetting, "Connection"))
        {
            foreach (var guid in CimValues.ExtractGuids(connection))
            {
                if (switchNameByPortGuid.TryGetValue(guid, out var switchName))
                {
                    return switchName;
                }
            }
        }

        return string.Empty;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    // ------------------------------------------------------------------
    // Phase 4: virtual switches, VLAN, VM storage drives.
    // ------------------------------------------------------------------

    /// <summary>
    /// One virtual-switch row from precomputed native facts. SwitchType is
    /// derived from live port associations (never guessed): an external
    /// uplink port means External, a host port without uplink means Internal,
    /// neither means Private. AllowManagementOS follows the same facts
    /// (internal switches always serve the host); null when undeterminable.
    /// </summary>
    public static VmSwitchRow MapSwitchRow(
        string node,
        string name,
        bool hasExternalPort,
        bool hasInternalPort,
        string physicalDescription)
    {
        var switchType = hasExternalPort ? "External"
            : hasInternalPort ? "Internal" : "Private";
        bool? allowManagementOs = switchType switch
        {
            "External" => hasInternalPort,
            "Internal" => true,
            "Private" => false,
            _ => null,
        };
        return new VmSwitchRow(node, name, switchType, allowManagementOs, physicalDescription);
    }

    /// <summary>
    /// VLAN operation-mode mapping (MS docs for
    /// Msvm_EthernetSwitchPortVlanSettingData: 1 Access, 2 Trunk, 3 Private).
    /// Get-VMNetworkAdapterVlan reports "Untagged" when no explicit mode is
    /// set (provider default 0 / missing setting).
    /// </summary>
    public static string MapVlanOperationMode(object? value)
    {
        long? code = value is null ? null : ToInt64(value);
        return code switch
        {
            null or 0 => "Untagged",
            1 => "Access",
            2 => "Trunk",
            3 => "Private",
            _ => $"Unknown ({code})",
        };
    }

    /// <summary>
    /// One VLAN row from a Msvm_EthernetSwitchPortVlanSettingData bag.
    /// Trunk members (TrunkVlanIdArray) join with ", " for display.
    /// </summary>
    public static VmVlanRow MapVlanRow(
        string node,
        string vmName,
        string adapterName,
        IReadOnlyDictionary<string, object?> setting)
    {
        return new VmVlanRow(
            node,
            vmName,
            adapterName,
            MapVlanOperationMode(setting.TryGetValue("OperationMode", out var mode) ? mode : null),
            ToNullableInt(setting.TryGetValue("AccessVlanId", out var access) ? access : null),
            ToNullableInt(setting.TryGetValue("NativeVlanId", out var native) ? native : null),
            string.Join(", ", CimValues.GetStringArray(setting, "TrunkVlanIdArray")));
    }

    /// <summary>
    /// Indexes VLAN setting bags by every GUID embedded in their InstanceID
    /// (the switch-port GUID), so current-NIC Connection[] references join.
    /// </summary>
    public static Dictionary<string, IReadOnlyDictionary<string, object?>> IndexVlanSettingsByPort(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> settings)
    {
        var index = new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
        foreach (var setting in settings)
        {
            foreach (var guid in CimValues.ExtractGuids(CimValues.GetString(setting, "InstanceID")))
            {
                index[guid] = setting;
            }
        }

        return index;
    }

    /// <summary>
    /// Port GUIDs referenced by a current NIC setting's live Connection[].
    /// </summary>
    public static IReadOnlyList<string> GetConnectionPortGuids(IReadOnlyDictionary<string, object?> nicSetting)
    {
        var guids = new List<string>();
        foreach (var connection in CimValues.GetStringArray(nicSetting, "Connection"))
        {
            guids.AddRange(CimValues.ExtractGuids(connection));
        }

        return guids;
    }

    /// <summary>
    /// Get-VMHardDiskDrive semantics over the CURRENT VSSD resource graph
    /// (verified against the live DOS VM graph):
    /// Hard Drive device (Msvm_ResourceAllocationSettingData, ResourceType 17,
    /// "...Synthetic Disk Drive") → child Hard Disk Image
    /// (Msvm_StorageAllocationSettingData, ResourceType 31, subtype EXACTLY
    /// "Microsoft:Hyper-V:Virtual Hard Disk", Parent = drive) → HostResource
    /// VHD path. Controller resolves through the drive's Parent controller
    /// RASD of ANY setting class (IDE ResourceType 5, SCSI ResourceType 6):
    /// "{SCSI|IDE} {number}:{location}".
    /// Floppy (14 / Virtual Floppy Disk), DVD (16 / Virtual CD/DVD Disk),
    /// ISO and VFD images are structurally excluded — never by extension.
    /// </summary>
    public sealed record VmDriveInfo(string Controller, string Path);

    public static IReadOnlyList<VmDriveInfo> SelectCurrentVmDrives(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> vssdComponents)
    {
        var components = vssdComponents.ToList();

        var controllers = new Dictionary<string, (string Type, long Number)>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in components)
        {
            var subType = CimValues.GetString(component, "ResourceSubType");
            string? kind = subType.Contains("SCSI", StringComparison.OrdinalIgnoreCase) ? "SCSI"
                : subType.Contains("IDE", StringComparison.OrdinalIgnoreCase) ? "IDE" : null;
            if (kind is null)
            {
                continue;
            }

            var instanceId = CimValues.GetString(component, "InstanceID");
            if (string.IsNullOrEmpty(instanceId))
            {
                continue;
            }

            var number = ToNullableInt(component.TryGetValue("Address", out var address) ? address : null) ?? 0;
            controllers[instanceId] = (kind, number);
        }

        var drives = new List<VmDriveInfo>();
        foreach (var drive in components.Where(IsHardDriveDevice))
        {
            var driveId = CimValues.GetString(drive, "InstanceID");
            var image = components.FirstOrDefault(c => IsHardDiskImageForDrive(c, driveId));
            if (image is null)
            {
                continue;
            }

            var path = CimValues.GetStringArray(image, "HostResource").FirstOrDefault() ?? string.Empty;
            drive.TryGetValue("Parent", out var driveParentRaw);
            var controller = ResolveControllerValue(driveParentRaw, controllers);
            var location = ToNullableInt(
                drive.TryGetValue("AddressOnParent", out var addressOnParent) ? addressOnParent : null) ?? 0;
            drives.Add(new VmDriveInfo($"{controller.Type} {controller.Number}:{location}", path));
        }

        return drives;
    }

    private static bool IsHardDriveDevice(IReadOnlyDictionary<string, object?> component)
    {
        if (!string.Equals(CimValues.GetString(component, "__Class"),
                "Msvm_ResourceAllocationSettingData", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (CimValues.GetLong(component, "ResourceType") != 17)
        {
            return false;
        }

        var subType = CimValues.GetString(component, "ResourceSubType");
        return subType.Contains("Disk Drive", StringComparison.OrdinalIgnoreCase)
            && !subType.Contains("Dvd", StringComparison.OrdinalIgnoreCase)
            && !subType.Contains("Diskette", StringComparison.OrdinalIgnoreCase)
            && !subType.Contains("Floppy", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHardDiskImageForDrive(
        IReadOnlyDictionary<string, object?> component, string driveInstanceId)
    {
        if (!string.Equals(CimValues.GetString(component, "__Class"),
                "Msvm_StorageAllocationSettingData", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (CimValues.GetLong(component, "ResourceType") != 31)
        {
            return false;
        }

        // EXACT subtype: "Virtual Floppy Disk" and "Virtual CD/DVD Disk" share
        // ResourceType 31 but must never become hard disks.
        if (!string.Equals(CimValues.GetString(component, "ResourceSubType"),
                "Microsoft:Hyper-V:Virtual Hard Disk", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        component.TryGetValue("Parent", out var parentRaw);
        return ParentReferencesValue(parentRaw, driveInstanceId);
    }

    /// <summary>
    /// Parent→child reference on CANONICAL InstanceIDs (see ReferenceIds):
    /// exact equality first, then the documented nesting where the child
    /// InstanceID extends the parent path ("...\\0" → "...\\0\\0\\D").
    /// Raw WMI object paths are canonicalized before comparison — a full
    /// reference path never fails against its plain InstanceID.
    /// GUID-overlap merging is deliberately NOT used here — it caused the
    /// historical cross-VSSD phantom joins.
    /// </summary>
    public static bool ParentReferences(string? parent, string? childOrParentId)
    {
        var canonicalParent = ReferenceIds.NormalizeReferenceInstanceId(parent);
        var canonicalId = ReferenceIds.NormalizeReferenceInstanceId(childOrParentId);
        if (string.IsNullOrEmpty(canonicalParent) || string.IsNullOrEmpty(canonicalId))
        {
            return false;
        }

        return string.Equals(canonicalParent, canonicalId, StringComparison.OrdinalIgnoreCase)
            || canonicalId.StartsWith(canonicalParent + "\\", StringComparison.OrdinalIgnoreCase)
            || canonicalParent.StartsWith(canonicalId + "\\", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reference-typed comparison against a RAW CIM property value: a live
    /// CimInstance contributes its InstanceID key directly (Convert.ToString
    /// on such an object is NOT sufficient); text goes through path
    /// normalization.
    /// </summary>
    public static bool ParentReferencesValue(object? parentRaw, string? childOrParentId)
    {
        var canonicalParent = ReferenceIds.TryGetReferencedInstanceId(parentRaw);
        if (string.IsNullOrEmpty(canonicalParent))
        {
            return false;
        }

        return ParentReferences(canonicalParent, childOrParentId);
    }

    private static (string Type, long Number) ResolveController(
        string parent, Dictionary<string, (string Type, long Number)> controllers)
    {
        foreach (var (instanceId, controller) in controllers)
        {
            if (ParentReferences(parent, instanceId))
            {
                return controller;
            }
        }

        return ("Unknown", 0);
    }

    private static (string Type, long Number) ResolveControllerValue(
        object? parentRaw, Dictionary<string, (string Type, long Number)> controllers)
    {
        var canonicalParent = ReferenceIds.TryGetReferencedInstanceId(parentRaw);
        if (string.IsNullOrEmpty(canonicalParent))
        {
            return ("Unknown", 0);
        }

        return ResolveController(canonicalParent, controllers);
    }

    private static bool IsVhdPath(string path)
    {
        var lower = path.ToLowerInvariant();
        return lower.EndsWith(".vhd", StringComparison.Ordinal)
            || lower.EndsWith(".vhdx", StringComparison.Ordinal)
            || lower.EndsWith(".vhds", StringComparison.Ordinal)
            || lower.EndsWith(".avhd", StringComparison.Ordinal)
            || lower.EndsWith(".avhdx", StringComparison.Ordinal);
    }

    private static long? ToInt64(object? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception) when (value is string || value is IConvertible)
        {
            return null;
        }
    }

    private static int? ToNullableInt(object? value)
    {
        var number = ToInt64(value);
        if (number is null)
        {
            return null;
        }

        return number.Value is >= int.MinValue and <= int.MaxValue ? (int)number.Value : null;
    }

    /// <summary>
    /// Correlates merged-adapter GUIDs with guest IP configurations. The VM GUID
    /// itself is excluded from matching (every InstanceID on the VM embeds it,
    /// which would join every NIC to the first config). When the adapter carries
    /// no GUID, a single VM-level config is used as fallback; ambiguous cases
    /// yield no IPs rather than wrong IPs. Missing guest data never fails.
    /// </summary>
    private static string[] FindGuestIps(
        IReadOnlyList<string> adapterGuids,
        string? vmGuid,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> guestConfigs)
    {
        if (guestConfigs.Count == 0)
        {
            return [];
        }

        if (adapterGuids.Count > 0)
        {
            var set = new HashSet<string>(adapterGuids, StringComparer.OrdinalIgnoreCase);
            foreach (var config in guestConfigs)
            {
                var configGuids = CimValues.ExtractGuids(CimValues.GetString(config, "InstanceID"));
                if (configGuids.Any(g => set.Contains(g)))
                {
                    return CimValues.GetStringArray(config, "IPAddresses");
                }
            }

            return [];
        }

        if (vmGuid is not null)
        {
            var vmConfigs = guestConfigs
                .Where(c => CimValues.ExtractGuids(CimValues.GetString(c, "InstanceID"))
                    .Contains(vmGuid, StringComparer.OrdinalIgnoreCase))
                .ToList();
            if (vmConfigs.Count == 1)
            {
                return CimValues.GetStringArray(vmConfigs[0], "IPAddresses");
            }
        }

        return [];
    }
}
