using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.State;

namespace Pi1.HyperVToolkit.Core.Export;

// Single deterministic report-schema catalog. Every built-in ResultName
// published into IReportService maps to exactly one schema with the ordered
// VISIBLE columns of its WPF grid/report — never raw CLR reflection, so
// internal/additive fields (Rule, IsSensitive, *Bytes, Uptime, AdviceCount…)
// can never leak into exported files.
//
// Visible-column parity notes:
// - Memory/size grids render *Bytes via DataSizeConverter; export carries the
//   semantic GB doubles / percents instead (numbers stay numeric in JSON).
// - Uptime grids render UptimeText; export carries that same display string.
// - Advisor Recommendation is derived localized text (current UI language),
//   matching the grid's AdvisorRecommendationConverter output.
public static class ReportSchemas
{
    public const string FullVMReportName = "FullVMReport";

    private static readonly Dictionary<string, ReportSchema> Schemas =
        new(StringComparer.Ordinal)
        {
            ["Dashboard"] = new("Dashboard",
            [
                Col("Area", "H_Area", ReportColumnKind.Text, r => ((DashboardRow)r).Area),
                Col("Status", "H_Status", ReportColumnKind.Text, r => ((DashboardRow)r).Status),
                Col("Value", "H_Value", ReportColumnKind.Text, r => ((DashboardRow)r).Value),
            ]),
            ["VirtualMachines"] = VmSchema("VirtualMachines"),
            ["VMMemory"] = new("VMMemory",
            [
                Col("HostNode", "H_HostNode", ReportColumnKind.Text, r => ((VirtualMachineRow)r).HostNode),
                Col("VM", "H_VM", ReportColumnKind.Text, r => ((VirtualMachineRow)r).VM),
                Col("State", "H_State", ReportColumnKind.Text, r => ((VirtualMachineRow)r).State),
                Col("Dynamic", "H_DynamicMemory", ReportColumnKind.Boolean, r => ((VirtualMachineRow)r).Dynamic),
                Col("StartupGB", "H_StartupMemory", ReportColumnKind.Number, r => ((VirtualMachineRow)r).StartupGB),
                Col("MinimumGB", "H_MinimumMemory", ReportColumnKind.Number, r => ((VirtualMachineRow)r).MinimumGB),
                Col("MaximumGB", "H_MaximumMemory", ReportColumnKind.Number, r => ((VirtualMachineRow)r).MaximumGB),
                Col("AssignedGB", "H_AssignedMemory", ReportColumnKind.Number, r => ((VirtualMachineRow)r).AssignedGB),
                Col("DemandGB", "H_DemandMemory", ReportColumnKind.Number, r => ((VirtualMachineRow)r).DemandGB),
                Col("WasteGB", "H_MemoryDifference", ReportColumnKind.Number, r => ((VirtualMachineRow)r).WasteGB),
            ]),
            [FullVMReportName] = FullVmSchema(FullVMReportName),
            ["NodeHardware"] = new("NodeHardware",
            [
                Col("Node", "H_Node", ReportColumnKind.Text, r => ((NodeHardwareRow)r).Node),
                Col("Manufacturer", "H_Manufacturer", ReportColumnKind.Text, r => ((NodeHardwareRow)r).Manufacturer),
                Col("Model", "H_Model", ReportColumnKind.Text, r => ((NodeHardwareRow)r).Model),
                Col("OS", "H_OS", ReportColumnKind.Text, r => ((NodeHardwareRow)r).OS),
                Col("Version", "H_Build", ReportColumnKind.Text, r => ((NodeHardwareRow)r).Version),
                Col("Uptime", "H_Uptime", ReportColumnKind.Text, r => ((NodeHardwareRow)r).UptimeText),
                Col("CPUName", "H_CPUName", ReportColumnKind.Text, r => ((NodeHardwareRow)r).CPUName),
                Col("Sockets", "H_Sockets", ReportColumnKind.Number, r => ((NodeHardwareRow)r).Sockets),
                Col("Cores", "H_Cores", ReportColumnKind.Number, r => ((NodeHardwareRow)r).Cores),
                Col("LogicalCPU", "H_LogicalCPUs", ReportColumnKind.Number, r => ((NodeHardwareRow)r).LogicalCPU),
                Col("RAMGB", "H_Memory", ReportColumnKind.Number, r => ((NodeHardwareRow)r).RAMGB),
                Col("UsedRAMGB", "H_UsedMemory", ReportColumnKind.Number, r => ((NodeHardwareRow)r).UsedRAMGB),
                Col("FreeRAMGB", "H_FreeMemory", ReportColumnKind.Number, r => ((NodeHardwareRow)r).FreeRAMGB),
                Col("RAMUsedPct", "H_MemoryUsage", ReportColumnKind.Number, r => ((NodeHardwareRow)r).RAMUsedPct),
            ]),
            ["VMStorageMap"] = VmStorageSchema("VMStorageMap"),
            ["VMNetwork"] = new("VMNetwork",
            [
                Col("HostNode", "H_HostNode", ReportColumnKind.Text, r => ((VmNetworkRow)r).HostNode),
                Col("VMName", "H_VMName", ReportColumnKind.Text, r => ((VmNetworkRow)r).VMName),
                Col("IPv4", "H_IPv4", ReportColumnKind.Text, r => ((VmNetworkRow)r).IPv4),
                Col("MacAddress", "H_MacAddress", ReportColumnKind.Text, r => ((VmNetworkRow)r).MacAddress),
                Col("SwitchName", "H_SwitchName", ReportColumnKind.Text, r => ((VmNetworkRow)r).SwitchName),
                Col("AllIPs", "H_AllIPs", ReportColumnKind.Text, r => ((VmNetworkRow)r).AllIPs),
            ]),
            ["ClusterNodes"] = new("ClusterNodes",
            [
                Col("Name", "H_Name", ReportColumnKind.Text, r => ((ClusterNodeRow)r).Name),
                Col("State", "H_State", ReportColumnKind.Text, r => ((ClusterNodeRow)r).State),
                Col("DrainStatus", "H_DrainStatus", ReportColumnKind.Text, r => ((ClusterNodeRow)r).DrainStatus),
                Col("NodeWeight", "H_NodeWeight", ReportColumnKind.Text, r => ((ClusterNodeRow)r).NodeWeight),
                Col("FaultDomain", "H_FaultDomain", ReportColumnKind.Text, r => ((ClusterNodeRow)r).FaultDomain),
            ]),
            ["Advisor"] = new("Advisor",
            [
                Col("Severity", "H_Severity", ReportColumnKind.Text, r => ((AdvisorRow)r).Severity),
                Col("HostNode", "H_HostNode", ReportColumnKind.Text, r => ((AdvisorRow)r).HostNode),
                Col("VM", "H_VM", ReportColumnKind.Text, r => ((AdvisorRow)r).VM),
                Col("AssignedGB", "H_AssignedMemory", ReportColumnKind.Number, r => ((AdvisorRow)r).AssignedGB),
                Col("DemandGB", "H_DemandMemory", ReportColumnKind.Number, r => ((AdvisorRow)r).DemandGB),
                Col("WasteGB", "H_MemoryDifference", ReportColumnKind.Number, r => ((AdvisorRow)r).WasteGB),
                Col("Recommendation", "H_Recommendation", ReportColumnKind.Text, r => AdvisorExportText.For((AdvisorRow)r)),
            ]),
            ["CSVLowFree"] = new("CSVLowFree",
            [
                Col("Name", "H_Name", ReportColumnKind.Text, r => ((CsvRow)r).Name),
                Col("State", "H_State", ReportColumnKind.Text, r => ((CsvRow)r).State),
                Col("OwnerNode", "H_OwnerNode", ReportColumnKind.Text, r => ((CsvRow)r).OwnerNode),
                Col("SizeGB", "H_Size", ReportColumnKind.Number, r => ((CsvRow)r).SizeGB),
                Col("FreeGB", "H_FreeSpace", ReportColumnKind.Number, r => ((CsvRow)r).FreeGB),
                Col("UsedGB", "H_UsedSpace", ReportColumnKind.Number, r => ((CsvRow)r).UsedGB),
                Col("FreePercent", "H_FreeSpacePct", ReportColumnKind.Number, r => ((CsvRow)r).FreePercent),
                Col("Path", "H_Path", ReportColumnKind.Text, r => ((CsvRow)r).Path),
            ]),
            ["ResourcesNotOnline"] = new("ResourcesNotOnline",
            [
                Col("Name", "H_Name", ReportColumnKind.Text, r => ((ClusterResourceRow)r).Name),
                Col("State", "H_State", ReportColumnKind.Text, r => ((ClusterResourceRow)r).State),
                Col("OwnerGroup", "H_OwnerGroup", ReportColumnKind.Text, r => ((ClusterResourceRow)r).OwnerGroup),
                Col("ResourceType", "H_ResourceType", ReportColumnKind.Text, r => ((ClusterResourceRow)r).ResourceType),
                Col("OwnerNode", "H_OwnerNode", ReportColumnKind.Text, r => ((ClusterResourceRow)r).OwnerNode),
            ]),
            ["CSV"] = new("CSV",
            [
                Col("Name", "H_Name", ReportColumnKind.Text, r => ((CsvRow)r).Name),
                Col("State", "H_State", ReportColumnKind.Text, r => ((CsvRow)r).State),
                Col("OwnerNode", "H_OwnerNode", ReportColumnKind.Text, r => ((CsvRow)r).OwnerNode),
                Col("SizeGB", "H_Size", ReportColumnKind.Number, r => ((CsvRow)r).SizeGB),
                Col("FreeGB", "H_FreeSpace", ReportColumnKind.Number, r => ((CsvRow)r).FreeGB),
                Col("UsedGB", "H_UsedSpace", ReportColumnKind.Number, r => ((CsvRow)r).UsedGB),
                Col("FreePercent", "H_FreeSpacePct", ReportColumnKind.Number, r => ((CsvRow)r).FreePercent),
                Col("Path", "H_Path", ReportColumnKind.Text, r => ((CsvRow)r).Path),
            ]),
            ["NodeSummary"] = new("NodeSummary",
            [
                Col("Node", "H_Node", ReportColumnKind.Text, r => ((NodeSummaryRow)r).Node),
                Col("RunningVM", "H_RunningVMs", ReportColumnKind.Number, r => ((NodeSummaryRow)r).RunningVM),
                Col("OffVM", "H_OffVMs", ReportColumnKind.Number, r => ((NodeSummaryRow)r).OffVM),
                Col("VCpu", "H_vCPU", ReportColumnKind.Number, r => ((NodeSummaryRow)r).VCpu),
                Col("LogicalCPU", "H_LogicalCPUs", ReportColumnKind.Number, r => ((NodeSummaryRow)r).LogicalCPU),
                Col("RAMGB", "H_Memory", ReportColumnKind.Number, r => ((NodeSummaryRow)r).RAMGB),
                Col("FreeRAMGB", "H_FreeMemory", ReportColumnKind.Number, r => ((NodeSummaryRow)r).FreeRAMGB),
                Col("RAMUsedPct", "H_MemoryUsage", ReportColumnKind.Number, r => ((NodeSummaryRow)r).RAMUsedPct),
                Col("AssignedGB", "H_AssignedMemory", ReportColumnKind.Number, r => ((NodeSummaryRow)r).AssignedGB),
                Col("DemandGB", "H_DemandMemory", ReportColumnKind.Number, r => ((NodeSummaryRow)r).DemandGB),
                Col("WasteGB", "H_MemoryDifference", ReportColumnKind.Number, r => ((NodeSummaryRow)r).WasteGB),
            ]),
            ["VmSummary"] = new("VmSummary",
            [
                Col("Metric", "H_Metric", ReportColumnKind.Text, r => SummaryExportText.Metric(((VmSummaryRow)r).Metric)),
                Col("Value", "H_Value", ReportColumnKind.Number, r => ((VmSummaryRow)r).Value),
            ]),
            ["StorageSummary"] = new("StorageSummary",
            [
                Col("Area", "H_Area", ReportColumnKind.Text, r => ((StorageSummaryRow)r).Area),
                Col("Count", "H_Count", ReportColumnKind.Number, r => ((StorageSummaryRow)r).Count),
                Col("Status", "H_Status", ReportColumnKind.Text, r => ((StorageSummaryRow)r).Status),
            ]),
            ["StorageJobs"] = new("StorageJobs",
            [
                Col("Name", "H_Name", ReportColumnKind.Text, r => ((StorageJobRow)r).Name),
                Col("JobState", "H_JobState", ReportColumnKind.Text, r => ((StorageJobRow)r).JobState),
                Col("JobType", "H_JobType", ReportColumnKind.Text, r => ((StorageJobRow)r).JobType),
                Col("PercentComplete", "H_PercentComplete", ReportColumnKind.Number, r => ((StorageJobRow)r).PercentComplete),
                Col("BytesProcessed", "H_ProcessedData", ReportColumnKind.Number, r => ((StorageJobRow)r).BytesProcessed),
                Col("BytesTotal", "H_TotalData", ReportColumnKind.Number, r => ((StorageJobRow)r).BytesTotal),
                Col("ElapsedTime", "H_ElapsedTime", ReportColumnKind.Text, r => ((StorageJobRow)r).ElapsedText),
            ]),
            ["StoragePools"] = new("StoragePools",
            [
                Col("FriendlyName", "H_Name", ReportColumnKind.Text, r => ((StoragePoolRow)r).FriendlyName),
                Col("HealthStatus", "H_Status", ReportColumnKind.Text, r => ((StoragePoolRow)r).HealthStatus),
                Col("OperationalStatus", "H_OperationalStatus", ReportColumnKind.Text, r => ((StoragePoolRow)r).OperationalStatus),
                Col("SizeGB", "H_Size", ReportColumnKind.Number, r => ((StoragePoolRow)r).SizeGB),
                Col("AllocatedGB", "H_UsedSpace", ReportColumnKind.Number, r => ((StoragePoolRow)r).AllocatedGB),
                Col("FreeGB", "H_FreeSpace", ReportColumnKind.Number, r => ((StoragePoolRow)r).FreeGB),
            ]),
            ["VirtualDisks"] = new("VirtualDisks",
            [
                Col("FriendlyName", "H_Name", ReportColumnKind.Text, r => ((VirtualDiskRow)r).FriendlyName),
                Col("HealthStatus", "H_Status", ReportColumnKind.Text, r => ((VirtualDiskRow)r).HealthStatus),
                Col("OperationalStatus", "H_OperationalStatus", ReportColumnKind.Text, r => ((VirtualDiskRow)r).OperationalStatus),
                Col("ResiliencySettingName", "H_Resiliency", ReportColumnKind.Text, r => ((VirtualDiskRow)r).ResiliencySettingName),
                Col("ProvisioningType", "H_Provisioning", ReportColumnKind.Text, r => ((VirtualDiskRow)r).ProvisioningType),
                Col("SizeGB", "H_Size", ReportColumnKind.Number, r => ((VirtualDiskRow)r).SizeGB),
                Col("AllocatedGB", "H_UsedSpace", ReportColumnKind.Number, r => ((VirtualDiskRow)r).AllocatedGB),
            ]),
            ["PhysicalDisks"] = new("PhysicalDisks",
            [
                Col("FriendlyName", "H_Name", ReportColumnKind.Text, r => ((PhysicalDiskRow)r).FriendlyName),
                Col("MediaType", "H_MediaType", ReportColumnKind.Text, r => ((PhysicalDiskRow)r).MediaType),
                Col("BusType", "H_BusType", ReportColumnKind.Text, r => ((PhysicalDiskRow)r).BusType),
                Col("SizeGB", "H_Size", ReportColumnKind.Number, r => ((PhysicalDiskRow)r).SizeGB),
                Col("HealthStatus", "H_Status", ReportColumnKind.Text, r => ((PhysicalDiskRow)r).HealthStatus),
                Col("OperationalStatus", "H_OperationalStatus", ReportColumnKind.Text, r => ((PhysicalDiskRow)r).OperationalStatus),
                Col("CanPool", "H_CanPool", ReportColumnKind.Boolean, r => ((PhysicalDiskRow)r).CanPool),
                Col("Usage", "H_Usage", ReportColumnKind.Text, r => ((PhysicalDiskRow)r).Usage),
                Col("SerialNumber", "H_SerialNumber", ReportColumnKind.Text, r => ((PhysicalDiskRow)r).SerialNumber),
            ]),
            ["PhysicalDiskSummary"] = new("PhysicalDiskSummary",
            [
                Col("Group", "H_Group", ReportColumnKind.Text, r => ((PhysicalDiskSummaryRow)r).Group),
                Col("Count", "H_Count", ReportColumnKind.Number, r => ((PhysicalDiskSummaryRow)r).Count),
                Col("TotalGB", "H_Size", ReportColumnKind.Number, r => ((PhysicalDiskSummaryRow)r).TotalGB),
                Col("Healthy", "H_Healthy", ReportColumnKind.Number, r => ((PhysicalDiskSummaryRow)r).Healthy),
                Col("NotHealthy", "H_NotHealthy", ReportColumnKind.Number, r => ((PhysicalDiskSummaryRow)r).NotHealthy),
                Col("CanPool", "H_CanPool", ReportColumnKind.Number, r => ((PhysicalDiskSummaryRow)r).CanPool),
            ]),
            ["StorageVolumes"] = new("StorageVolumes",
            [
                Col("DriveLetter", "H_Drive", ReportColumnKind.Text, r => ((StorageVolumeRow)r).DriveLetter),
                Col("FileSystemLabel", "H_Label", ReportColumnKind.Text, r => ((StorageVolumeRow)r).FileSystemLabel),
                Col("FileSystem", "H_FileSystem", ReportColumnKind.Text, r => ((StorageVolumeRow)r).FileSystem),
                Col("HealthStatus", "H_Status", ReportColumnKind.Text, r => ((StorageVolumeRow)r).HealthStatus),
                Col("OperationalStatus", "H_OperationalStatus", ReportColumnKind.Text, r => ((StorageVolumeRow)r).OperationalStatus),
                Col("SizeGB", "H_Size", ReportColumnKind.Number, r => ((StorageVolumeRow)r).SizeGB),
                Col("FreeGB", "H_FreeSpace", ReportColumnKind.Number, r => ((StorageVolumeRow)r).FreeGB),
                Col("FreePercent", "H_FreeSpacePct", ReportColumnKind.Number, r => ((StorageVolumeRow)r).FreePercent),
            ]),
            ["VMStorageByCsv"] = new("VMStorageByCsv",
            [
                Col("CSV", "H_CSV", ReportColumnKind.Text, r => ((VmStorageByCsvRow)r).CSV),
                Col("VMCount", "H_VMCount", ReportColumnKind.Number, r => ((VmStorageByCsvRow)r).VMCount),
                Col("DiskCount", "H_DiskCount", ReportColumnKind.Number, r => ((VmStorageByCsvRow)r).DiskCount),
                Col("VHDSizeGB", "H_VHDSize", ReportColumnKind.Number, r => ((VmStorageByCsvRow)r).VHDSizeGB),
                Col("VHDFileGB", "H_VHDFileSize", ReportColumnKind.Number, r => ((VmStorageByCsvRow)r).VHDFileGB),
                Col("CSVFreeGB", "H_CSVFree", ReportColumnKind.Number, r => ((VmStorageByCsvRow)r).CSVFreeGB),
                Col("CSVFreePercent", "H_CSVFreePct", ReportColumnKind.Number, r => ((VmStorageByCsvRow)r).CSVFreePercent),
            ]),
            ["SelectedVmStorage"] = VmStorageSchema("SelectedVmStorage"),
            ["VMCheckpoints"] = new("VMCheckpoints",
            [
                Col("HostNode", "H_HostNode", ReportColumnKind.Text, r => ((CheckpointRow)r).HostNode),
                Col("VM", "H_VM", ReportColumnKind.Text, r => ((CheckpointRow)r).VM),
                Col("Name", "H_Name", ReportColumnKind.Text, r => ((CheckpointRow)r).Name),
                Col("Created", "H_Created", ReportColumnKind.Text, r => ReportDates.Format(((CheckpointRow)r).Created)),
                Col("Type", "H_Type", ReportColumnKind.Text, r => ((CheckpointRow)r).Type),
            ]),
            ["VMWithoutIP"] = new("VMWithoutIP",
            [
                Col("HostNode", "H_HostNode", ReportColumnKind.Text, r => ((VmNetworkRow)r).HostNode),
                Col("VMName", "H_VMName", ReportColumnKind.Text, r => ((VmNetworkRow)r).VMName),
                Col("MacAddress", "H_MacAddress", ReportColumnKind.Text, r => ((VmNetworkRow)r).MacAddress),
                Col("SwitchName", "H_SwitchName", ReportColumnKind.Text, r => ((VmNetworkRow)r).SwitchName),
            ]),
            ["NodeCapacity"] = new("NodeCapacity",
            [
                Col("Node", "H_Node", ReportColumnKind.Text, r => ((NodeCapacityRow)r).Node),
                Col("RunningVM", "H_RunningVMs", ReportColumnKind.Number, r => ((NodeCapacityRow)r).RunningVM),
                Col("OffVM", "H_OffVMs", ReportColumnKind.Number, r => ((NodeCapacityRow)r).OffVM),
                Col("VCpu", "H_vCPU", ReportColumnKind.Number, r => ((NodeCapacityRow)r).VCpu),
                Col("LogicalCPU", "H_LogicalCPUs", ReportColumnKind.Number, r => ((NodeCapacityRow)r).LogicalCPU),
                Col("RAMGB", "H_Memory", ReportColumnKind.Number, r => ((NodeCapacityRow)r).RAMGB),
                Col("FreeRAMGB", "H_FreeMemory", ReportColumnKind.Number, r => ((NodeCapacityRow)r).FreeRAMGB),
                Col("RAMUsedPct", "H_MemoryUsage", ReportColumnKind.Number, r => ((NodeCapacityRow)r).RAMUsedPct),
                Col("AssignedGB", "H_AssignedMemory", ReportColumnKind.Number, r => ((NodeCapacityRow)r).AssignedGB),
                Col("DemandGB", "H_DemandMemory", ReportColumnKind.Number, r => ((NodeCapacityRow)r).DemandGB),
                Col("WasteGB", "H_MemoryDifference", ReportColumnKind.Number, r => ((NodeCapacityRow)r).WasteGB),
                Col("AssignedPct", "H_AssignedPct", ReportColumnKind.Number, r => ((NodeCapacityRow)r).AssignedPct),
                Col("DemandPct", "H_DemandPct", ReportColumnKind.Number, r => ((NodeCapacityRow)r).DemandPct),
            ]),
            ["NodeVolumes"] = new("NodeVolumes",
            [
                Col("Node", "H_Node", ReportColumnKind.Text, r => ((NodeVolumeRow)r).Node),
                Col("Drive", "H_Drive", ReportColumnKind.Text, r => ((NodeVolumeRow)r).Drive),
                Col("Label", "H_Label", ReportColumnKind.Text, r => ((NodeVolumeRow)r).Label),
                Col("FileSystem", "H_FileSystem", ReportColumnKind.Text, r => ((NodeVolumeRow)r).FileSystem),
                Col("SizeGB", "H_Size", ReportColumnKind.Number, r => ((NodeVolumeRow)r).SizeGB),
                Col("FreeGB", "H_FreeSpace", ReportColumnKind.Number, r => ((NodeVolumeRow)r).FreeGB),
                Col("FreePercent", "H_FreeSpacePct", ReportColumnKind.Number, r => ((NodeVolumeRow)r).FreePercent),
            ]),
            ["NodeAdapters"] = new("NodeAdapters",
            [
                Col("Node", "H_Node", ReportColumnKind.Text, r => ((NodeNetworkAdapterRow)r).Node),
                Col("Description", "H_Adapter", ReportColumnKind.Text, r => ((NodeNetworkAdapterRow)r).Description),
                Col("IPv4", "H_IPv4", ReportColumnKind.Text, r => ((NodeNetworkAdapterRow)r).IPv4),
                Col("MAC", "H_MAC", ReportColumnKind.Text, r => ((NodeNetworkAdapterRow)r).MAC),
                Col("Gateway", "H_Gateway", ReportColumnKind.Text, r => ((NodeNetworkAdapterRow)r).Gateway),
                Col("DNS", "H_DNS", ReportColumnKind.Text, r => ((NodeNetworkAdapterRow)r).DNS),
                Col("DHCP", "H_DHCP", ReportColumnKind.Boolean, r => ((NodeNetworkAdapterRow)r).DHCP),
            ]),
            ["HostSettings"] = new("HostSettings",
            [
                Col("Node", "H_Node", ReportColumnKind.Text, r => ((HostSettingsRow)r).Node),
                Col("LogicalProcessorCount", "H_LogicalCPUs", ReportColumnKind.Number, r => ((HostSettingsRow)r).LogicalProcessorCount),
                Col("MemoryCapacityGB", "H_Memory", ReportColumnKind.Number, r => ((HostSettingsRow)r).MemoryCapacityGB),
                Col("FreeRAMGB", "H_FreeMemory", ReportColumnKind.Number, r => ((HostSettingsRow)r).FreeRAMGB),
                Col("RAMUsedPct", "H_MemoryUsage", ReportColumnKind.Number, r => ((HostSettingsRow)r).RAMUsedPct),
                Col("NumaSpanningEnabled", "H_NumaSpanning", ReportColumnKind.Boolean, r => ((HostSettingsRow)r).NumaSpanningEnabled),
                Col("MigrationEnabled", "H_LiveMigration", ReportColumnKind.Boolean, r => ((HostSettingsRow)r).MigrationEnabled),
                Col("MaxMigrations", "H_MaxMigrations", ReportColumnKind.Number, r => ((HostSettingsRow)r).MaxMigrations),
                Col("EnhancedSessionMode", "H_EnhancedSession", ReportColumnKind.Boolean, r => ((HostSettingsRow)r).EnhancedSessionMode),
                Col("VirtualMachinePath", "D_VMPathTitle", ReportColumnKind.Text, r => ((HostSettingsRow)r).VirtualMachinePath),
                Col("VirtualHardDiskPath", "D_VHDPathTitle", ReportColumnKind.Text, r => ((HostSettingsRow)r).VirtualHardDiskPath),
            ]),
            ["VmSwitches"] = new("VmSwitches",
            [
                Col("HostNode", "H_HostNode", ReportColumnKind.Text, r => ((VmSwitchRow)r).HostNode),
                Col("Name", "H_Name", ReportColumnKind.Text, r => ((VmSwitchRow)r).Name),
                Col("SwitchType", "H_SwitchType", ReportColumnKind.Text, r => ((VmSwitchRow)r).SwitchType),
                Col("AllowManagementOS", "H_AllowMgmtOS", ReportColumnKind.Boolean, r => ((VmSwitchRow)r).AllowManagementOS),
                Col("NetworkAdapter", "H_NetworkAdapter", ReportColumnKind.Text, r => ((VmSwitchRow)r).NetAdapterInterfaceDescription),
            ]),
            ["VmVlans"] = new("VmVlans",
            [
                Col("HostNode", "H_HostNode", ReportColumnKind.Text, r => ((VmVlanRow)r).HostNode),
                Col("VMName", "H_VMName", ReportColumnKind.Text, r => ((VmVlanRow)r).VMName),
                Col("VMNetworkAdapterName", "H_AdapterName", ReportColumnKind.Text, r => ((VmVlanRow)r).VMNetworkAdapterName),
                Col("OperationMode", "H_OperationMode", ReportColumnKind.Text, r => ((VmVlanRow)r).OperationMode),
                Col("AccessVlanId", "H_AccessVlan", ReportColumnKind.Number, r => ((VmVlanRow)r).AccessVlanId),
                Col("NativeVlanId", "H_NativeVlan", ReportColumnKind.Number, r => ((VmVlanRow)r).NativeVlanId),
                Col("AllowedVlanIdList", "H_AllowedVlans", ReportColumnKind.Text, r => ((VmVlanRow)r).AllowedVlanIdList),
            ]),
            ["ClusterRoles"] = new("ClusterRoles",
            [
                Col("Name", "H_Name", ReportColumnKind.Text, r => ((ClusterGroupRow)r).Name),
                Col("State", "H_State", ReportColumnKind.Text, r => ((ClusterGroupRow)r).State),
                Col("OwnerNode", "H_OwnerNode", ReportColumnKind.Text, r => ((ClusterGroupRow)r).OwnerNode),
                Col("GroupType", "H_GroupType", ReportColumnKind.Text, r => ((ClusterGroupRow)r).GroupType),
                Col("Priority", "H_Priority", ReportColumnKind.Text, r => ((ClusterGroupRow)r).Priority),
            ]),
            ["ClusterResources"] = new("ClusterResources",
            [
                Col("Name", "H_Name", ReportColumnKind.Text, r => ((ClusterResourceRow)r).Name),
                Col("State", "H_State", ReportColumnKind.Text, r => ((ClusterResourceRow)r).State),
                Col("OwnerGroup", "H_OwnerGroup", ReportColumnKind.Text, r => ((ClusterResourceRow)r).OwnerGroup),
                Col("ResourceType", "H_ResourceType", ReportColumnKind.Text, r => ((ClusterResourceRow)r).ResourceType),
                Col("OwnerNode", "H_OwnerNode", ReportColumnKind.Text, r => ((ClusterResourceRow)r).OwnerNode),
            ]),
            ["ClusterNetworks"] = new("ClusterNetworks",
            [
                Col("Name", "H_Name", ReportColumnKind.Text, r => ((ClusterNetworkRow)r).Name),
                Col("State", "H_State", ReportColumnKind.Text, r => ((ClusterNetworkRow)r).State),
                Col("Role", "H_Role", ReportColumnKind.Text, r => ((ClusterNetworkRow)r).Role),
                Col("Address", "H_Address", ReportColumnKind.Text, r => ((ClusterNetworkRow)r).Address),
                Col("AddressMask", "H_AddressMask", ReportColumnKind.Text, r => ((ClusterNetworkRow)r).AddressMask),
                Col("Metric", "H_Metric", ReportColumnKind.Number, r => ((ClusterNetworkRow)r).Metric),
                Col("AutoMetric", "H_AutoMetric", ReportColumnKind.Boolean, r => ((ClusterNetworkRow)r).AutoMetric),
            ]),
            ["ClusterQuorum"] = new("ClusterQuorum",
            [
                Col("QuorumType", "H_QuorumType", ReportColumnKind.Text, r => ((QuorumInfo)r).QuorumType),
                Col("QuorumResource", "H_QuorumResource", ReportColumnKind.Text, r => ((QuorumInfo)r).QuorumResource),
            ]),
            ["ClusterWitness"] = new("ClusterWitness",
            [
                Col("Section", "H_Section", ReportColumnKind.Text, r => ((WitnessRow)r).Section),
                Col("Name", "H_Name", ReportColumnKind.Text, r => ((WitnessRow)r).Name),
                Col("Type", "H_Type", ReportColumnKind.Text, r => ((WitnessRow)r).Type),
                Col("State", "H_State", ReportColumnKind.Text, r => ((WitnessRow)r).State),
                Col("OwnerGroup", "H_OwnerGroup", ReportColumnKind.Text, r => ((WitnessRow)r).OwnerGroup),
                Col("OwnerNode", "H_OwnerNode", ReportColumnKind.Text, r => ((WitnessRow)r).OwnerNode),
                Col("Detail", "H_Detail", ReportColumnKind.Text, r => ((WitnessRow)r).Detail),
            ]),
            ["ClusterEvents"] = new("ClusterEvents",
            [
                Col("TimeCreated", "H_Time", ReportColumnKind.Text, r => ReportDates.Format(((ClusterEventRow)r).TimeCreated)),
                Col("Id", "H_Id", ReportColumnKind.Number, r => ((ClusterEventRow)r).Id),
                Col("Level", "H_Level", ReportColumnKind.Text, r => ((ClusterEventRow)r).Level),
                Col("Provider", "H_Provider", ReportColumnKind.Text, r => ((ClusterEventRow)r).Provider),
                Col("Message", "H_Message", ReportColumnKind.Text, r => ((ClusterEventRow)r).Message),
            ]),
            ["ClusterPlacement"] = new("ClusterPlacement",
            [
                Col("VMGroup", "H_VMGroup", ReportColumnKind.Text, r => ((VmPlacementRow)r).VMGroup),
                Col("State", "H_State", ReportColumnKind.Text, r => ((VmPlacementRow)r).State),
                Col("OwnerNode", "H_OwnerNode", ReportColumnKind.Text, r => ((VmPlacementRow)r).OwnerNode),
                Col("Priority", "H_Priority", ReportColumnKind.Text, r => ((VmPlacementRow)r).Priority),
                Col("PreferredOwners", "H_PreferredOwners", ReportColumnKind.Text, r => ((VmPlacementRow)r).PreferredOwnersDisplay),
                Col("PossibleOwners", "H_PossibleOwners", ReportColumnKind.Text, r => ((VmPlacementRow)r).PossibleOwnersDisplay),
                Col("AntiAffinity", "H_AntiAffinity", ReportColumnKind.Text, r => ((VmPlacementRow)r).AntiAffinity),
            ]),
            ["ClusterPreferredOwners"] = new("ClusterPreferredOwners",
            [
                Col("VMGroup", "H_VMGroup", ReportColumnKind.Text, r => ((VmPlacementRow)r).VMGroup),
                Col("OwnerNode", "H_OwnerNode", ReportColumnKind.Text, r => ((VmPlacementRow)r).OwnerNode),
                Col("PreferredOwners", "H_PreferredOwners", ReportColumnKind.Text, r => ((VmPlacementRow)r).PreferredOwnersDisplay),
                Col("PossibleOwners", "H_PossibleOwners", ReportColumnKind.Text, r => ((VmPlacementRow)r).PossibleOwnersDisplay),
                Col("AutoFailback", "H_AutoFailback", ReportColumnKind.Text, r => ((VmPlacementRow)r).AutoFailback),
                Col("FailbackWindow", "H_FailbackWindow", ReportColumnKind.Text, r => ((VmPlacementRow)r).FailbackWindow),
            ]),
            ["VmDistribution"] = new("VmDistribution",
            [
                Col("OwnerNode", "H_OwnerNode", ReportColumnKind.Text, r => ((VmDistributionRow)r).OwnerNode),
                Col("VMGroups", "H_VMGroups", ReportColumnKind.Number, r => ((VmDistributionRow)r).VMGroups),
                Col("RunningVM", "H_RunningVMs", ReportColumnKind.Number, r => ((VmDistributionRow)r).RunningVM),
                Col("VCpu", "H_vCPU", ReportColumnKind.Number, r => ((VmDistributionRow)r).VCpu),
                Col("AssignedGB", "H_AssignedMemory", ReportColumnKind.Number, r => ((VmDistributionRow)r).AssignedGB),
                Col("DemandGB", "H_DemandMemory", ReportColumnKind.Number, r => ((VmDistributionRow)r).DemandGB),
                Col("WasteGB", "H_MemoryDifference", ReportColumnKind.Number, r => ((VmDistributionRow)r).WasteGB),
                Col("HighPriority", "H_HighPriority", ReportColumnKind.Number, r => ((VmDistributionRow)r).HighPriority),
            ]),
            ["PlacementAdvice"] = new("PlacementAdvice",
            [
                Col("Severity", "H_Severity", ReportColumnKind.Text, r => ((PlacementAdviceRow)r).Severity),
                Col("Node", "H_Node", ReportColumnKind.Text, r => ((PlacementAdviceRow)r).Node),
                Col("RunningVM", "H_RunningVMs", ReportColumnKind.Number, r => ((PlacementAdviceRow)r).RunningVM),
                Col("VCpu", "H_vCPU", ReportColumnKind.Number, r => ((PlacementAdviceRow)r).VCpu),
                Col("RAMGB", "H_Memory", ReportColumnKind.Number, r => ((PlacementAdviceRow)r).RAMGB),
                Col("FreeRAMGB", "H_FreeMemory", ReportColumnKind.Number, r => ((PlacementAdviceRow)r).FreeRAMGB),
                Col("DemandGB", "H_DemandMemory", ReportColumnKind.Number, r => ((PlacementAdviceRow)r).DemandGB),
                Col("DemandPct", "H_DemandPct", ReportColumnKind.Number, r => ((PlacementAdviceRow)r).DemandPct),
                Col("AssignedGB", "H_AssignedMemory", ReportColumnKind.Number, r => ((PlacementAdviceRow)r).AssignedGB),
                Col("AssignedPct", "H_AssignedPct", ReportColumnKind.Number, r => ((PlacementAdviceRow)r).AssignedPct),
                Col("Recommendation", "H_Recommendation", ReportColumnKind.Text, r => PlacementExportText.For((PlacementAdviceRow)r)),
            ]),
            ["FailoverSimulation"] = new("FailoverSimulation",
            [
                Col("FailedNode", "H_FailedNode", ReportColumnKind.Text, r => ((FailoverRow)r).FailedNode),
                Col("TargetNode", "H_TargetNode", ReportColumnKind.Text, r => ((FailoverRow)r).TargetNode),
                Col("VMsToMove", "H_VMsToMove", ReportColumnKind.Number, r => ((FailoverRow)r).VMsToMove),
                Col("MoveDemandGB", "H_MoveDemand", ReportColumnKind.Number, r => ((FailoverRow)r).MoveDemandGB),
                Col("MoveAssignedGB", "H_MoveAssigned", ReportColumnKind.Number, r => ((FailoverRow)r).MoveAssignedGB),
                Col("MoveVCpu", "H_MoveVCpu", ReportColumnKind.Number, r => ((FailoverRow)r).MoveVCpu),
                Col("TargetRAMGB", "H_TargetRAM", ReportColumnKind.Number, r => ((FailoverRow)r).TargetRAMGB),
                Col("TargetFreeOSGB", "H_TargetFree", ReportColumnKind.Number, r => ((FailoverRow)r).TargetFreeOSGB),
                Col("AfterDemandGB", "H_AfterDemand", ReportColumnKind.Number, r => ((FailoverRow)r).AfterDemandGB),
                Col("AfterDemandPct", "H_AfterDemandPct", ReportColumnKind.Number, r => ((FailoverRow)r).AfterDemandPct),
                Col("FreeAfterDemandGB", "H_FreeAfter", ReportColumnKind.Number, r => ((FailoverRow)r).FreeAfterDemandGB),
                Col("Status", "H_Status", ReportColumnKind.Text, r => ((FailoverRow)r).Status),
                Col("Advice", "H_Advice", ReportColumnKind.Text, r => FailoverExportText.For((FailoverRow)r)),
            ]),
            ["HealthChecks"] = new("HealthChecks",
            [
                Col("Area", "H_Area", ReportColumnKind.Text, r => ((HealthCheckRow)r).Area),
                Col("Status", "H_Status", ReportColumnKind.Text, r => ((HealthCheckRow)r).Status),
                Col("Detail", "H_Detail", ReportColumnKind.Text, r => ((HealthCheckRow)r).Detail),
            ]),
        };

    // VM Overview grid columns EXACTLY (NOT the FullVMReport): the Overview
    // table shows no Dynamic/Startup/Minimum/Maximum columns. Raw *Bytes
    // backing fields are intentionally excluded.
    private static ReportSchema VmSchema(string name) => new(name,
    [
        Col("HostNode", "H_HostNode", ReportColumnKind.Text, r => ((VirtualMachineRow)r).HostNode),
        Col("VM", "H_VM", ReportColumnKind.Text, r => ((VirtualMachineRow)r).VM),
        Col("State", "H_State", ReportColumnKind.Text, r => ((VirtualMachineRow)r).State),
        Col("CPU", "H_CPU", ReportColumnKind.Number, r => ((VirtualMachineRow)r).CPU),
        Col("AssignedGB", "H_AssignedMemory", ReportColumnKind.Number, r => ((VirtualMachineRow)r).AssignedGB),
        Col("DemandGB", "H_DemandMemory", ReportColumnKind.Number, r => ((VirtualMachineRow)r).DemandGB),
        Col("WasteGB", "H_MemoryDifference", ReportColumnKind.Number, r => ((VirtualMachineRow)r).WasteGB),
        Col("IPv4", "H_IPv4", ReportColumnKind.Text, r => ((VirtualMachineRow)r).IPv4),
        Col("MAC", "H_MAC", ReportColumnKind.Text, r => ((VirtualMachineRow)r).MAC),
        Col("Switch", "H_Switch", ReportColumnKind.Text, r => ((VirtualMachineRow)r).Switch),
        Col("Uptime", "H_Uptime", ReportColumnKind.Text, r => ((VirtualMachineRow)r).UptimeText),
    ]);

    // Legacy FullVMReport: the explicit 15-column VM base report from the
    // Export page (Get-PiVMBaseRows semantics). Never merged with the
    // Overview/Memory visible tables.
    private static ReportSchema FullVmSchema(string name) => new(name,
    [
        Col("HostNode", "H_HostNode", ReportColumnKind.Text, r => ((VirtualMachineRow)r).HostNode),
        Col("VM", "H_VM", ReportColumnKind.Text, r => ((VirtualMachineRow)r).VM),
        Col("State", "H_State", ReportColumnKind.Text, r => ((VirtualMachineRow)r).State),
        Col("CPU", "H_CPU", ReportColumnKind.Number, r => ((VirtualMachineRow)r).CPU),
        Col("AssignedGB", "H_AssignedMemory", ReportColumnKind.Number, r => ((VirtualMachineRow)r).AssignedGB),
        Col("DemandGB", "H_DemandMemory", ReportColumnKind.Number, r => ((VirtualMachineRow)r).DemandGB),
        Col("WasteGB", "H_MemoryDifference", ReportColumnKind.Number, r => ((VirtualMachineRow)r).WasteGB),
        Col("IPv4", "H_IPv4", ReportColumnKind.Text, r => ((VirtualMachineRow)r).IPv4),
        Col("MAC", "H_MAC", ReportColumnKind.Text, r => ((VirtualMachineRow)r).MAC),
        Col("Switch", "H_Switch", ReportColumnKind.Text, r => ((VirtualMachineRow)r).Switch),
        Col("Uptime", "H_Uptime", ReportColumnKind.Text, r => ((VirtualMachineRow)r).UptimeText),
        Col("Dynamic", "H_DynamicMemory", ReportColumnKind.Boolean, r => ((VirtualMachineRow)r).Dynamic),
        Col("StartupGB", "H_StartupMemory", ReportColumnKind.Number, r => ((VirtualMachineRow)r).StartupGB),
        Col("MinimumGB", "H_MinimumMemory", ReportColumnKind.Number, r => ((VirtualMachineRow)r).MinimumGB),
        Col("MaximumGB", "H_MaximumMemory", ReportColumnKind.Number, r => ((VirtualMachineRow)r).MaximumGB),
    ]);

    // VM storage map columns, shared by the full map and the selected-VM view.
    private static ReportSchema VmStorageSchema(string name) => new(name,
    [
        Col("HostNode", "H_HostNode", ReportColumnKind.Text, r => ((VmStorageRow)r).HostNode),
        Col("VM", "H_VM", ReportColumnKind.Text, r => ((VmStorageRow)r).VM),
        Col("State", "H_State", ReportColumnKind.Text, r => ((VmStorageRow)r).State),
        Col("Controller", "H_Controller", ReportColumnKind.Text, r => ((VmStorageRow)r).Controller),
        Col("VHDFormat", "H_VHDFormat", ReportColumnKind.Text, r => ((VmStorageRow)r).VHDFormat),
        Col("VHDType", "H_VHDType", ReportColumnKind.Text, r => ((VmStorageRow)r).VHDType),
        Col("VHDSizeGB", "H_VHDSize", ReportColumnKind.Number, r => ((VmStorageRow)r).VHDSizeGB),
        Col("VHDFileGB", "H_VHDFileSize", ReportColumnKind.Number, r => ((VmStorageRow)r).VHDFileGB),
        Col("CSV", "H_CSV", ReportColumnKind.Text, r => ((VmStorageRow)r).CSV),
        Col("CSVFreeGB", "H_CSVFree", ReportColumnKind.Number, r => ((VmStorageRow)r).CSVFreeGB),
        Col("CSVFreePercent", "H_CSVFreePct", ReportColumnKind.Number, r => ((VmStorageRow)r).CSVFreePercent),
        Col("Path", "H_Path", ReportColumnKind.Text, r => ((VmStorageRow)r).Path),
    ]);

    private static ReportColumn Col(
        string id, string headerKey, ReportColumnKind kind, Func<object, object?> getValue) =>
        new(id, headerKey, kind, getValue);

    public static bool TryGet(string name, out ReportSchema schema) =>
        Schemas.TryGetValue(name, out schema!);

    /// <summary>Every built-in stable ResultName, including the export-only FullVMReport.</summary>
    public static IReadOnlyList<string> BuiltInNames => Schemas.Keys.ToList();

    // Human-friendly display: canonical ResultName -> localized tab/section
    // title key. CSV/JSON identifiers stay canonical; only the Export page
    // label (and HTML <title>) uses this. Unknown names fall back to the
    // canonical name itself.
    private static readonly Dictionary<string, string> DisplayKeys =
        new(StringComparer.Ordinal)
        {
            ["Dashboard"] = "Nav_Dashboard",
            ["NodeSummary"] = "Tab_Nodes",
            ["VmSummary"] = "Tab_VMSummary",
            ["StorageSummary"] = "Tab_StorageSummary",
            ["StorageJobs"] = "Tab_Jobs",
            ["CSV"] = "Tab_CSV",
            ["VirtualMachines"] = "Nav_VMs",
            ["VMMemory"] = "Tab_Memory",
            [FullVMReportName] = "Exp_FullTitle",
            ["NodeHardware"] = "Tab_HW",
            ["NodeCapacity"] = "Tab_Capacity",
            ["NodeVolumes"] = "Tab_Volumes",
            ["NodeAdapters"] = "Tab_Network",
            ["HostSettings"] = "Tab_Host",
            ["VMStorageMap"] = "Tab_VMMap",
            ["VMStorageByCsv"] = "Tab_ByCSV",
            ["SelectedVmStorage"] = "Tab_SelectedVM",
            ["StoragePools"] = "Tab_Pools",
            ["VirtualDisks"] = "Tab_VirtualDisks",
            ["PhysicalDisks"] = "Tab_PhysicalDisks",
            ["PhysicalDiskSummary"] = "Tab_DiskSummary",
            ["StorageVolumes"] = "Tab_Volumes",
            ["VMNetwork"] = "Tab_Adapters",
            ["VMWithoutIP"] = "Tab_WithoutIP",
            ["VMCheckpoints"] = "Tab_Checkpoints",
            ["VmSwitches"] = "Tab_Switches",
            ["VmVlans"] = "Tab_VLAN",
            ["ClusterNodes"] = "Tab_Nodes",
            ["ClusterRoles"] = "Tab_Roles",
            ["ClusterResources"] = "Tab_Resources",
            ["ClusterNetworks"] = "Tab_Networks",
            ["ClusterQuorum"] = "Tab_Quorum",
            ["ClusterWitness"] = "Tab_Witness",
            ["ClusterEvents"] = "Tab_Events",
            ["ClusterPlacement"] = "Tab_Ownership",
            ["ClusterPreferredOwners"] = "Tab_PreferredOwners",
            ["VmDistribution"] = "Tab_Distribution",
            ["PlacementAdvice"] = "Tab_PlacementAdvisor",
            ["FailoverSimulation"] = "Tab_Failover",
            ["HealthChecks"] = "Tab_HealthScore",
            ["Advisor"] = "Tab_Advisor",
            ["CSVLowFree"] = "Tab_CsvLowFree",
            ["ResourcesNotOnline"] = "Tab_ResourcesNotOnline",
        };

    public static string DisplayName(string name, Localization.AppLanguage language)
    {
        if (DisplayKeys.TryGetValue(name, out var key))
        {
            return Localization.UiStrings.Get(language, key);
        }

        return name;
    }

    public static bool HasDisplayName(string name) => DisplayKeys.ContainsKey(name);
}

// Deterministic DateTime rendering for export ("yyyy-MM-dd HH:mm:ss",
// InvariantCulture); null renders empty. Grids show localized dates, but
// reports keep this unambiguous sortable form.
internal static class ReportDates
{
    internal static string Format(DateTime? value) =>
        value?.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
}

// Localized VM-summary metric for export, mirroring StatusValueConverter's
// Metric branch; unknown metrics pass through canonically.
internal static class SummaryExportText
{
    internal static string Metric(string canonical) => canonical switch
    {
        "Running" => LocalizationService.Instance["M_Running"],
        "Off" => LocalizationService.Instance["M_Off"],
        "Total VM" => LocalizationService.Instance["M_TotalVM"],
        "AssignedGB Running" => LocalizationService.Instance["M_AssignedRunning"],
        "DemandGB Running" => LocalizationService.Instance["M_DemandRunning"],
        "WasteGB Running" => LocalizationService.Instance["M_WasteRunning"],
        _ => canonical,
    };
}

// Localized Placement Advisor recommendation for export (current UI
// language), mirroring PlacementAdviceConverter exactly.
internal static class PlacementExportText
{
    internal static string For(PlacementAdviceRow row)
    {
        var loc = LocalizationService.Instance;
        return row.Advice switch
        {
            PlacementAdviceKind.Balanced => loc["Adv_Balanced"],
            PlacementAdviceKind.HighDemand => loc["Adv_HighDemand"],
            PlacementAdviceKind.HighAssigned => loc["Adv_HighAssigned"],
            PlacementAdviceKind.NoPreferredOwners => string.Format(
                System.Globalization.CultureInfo.InvariantCulture, loc["Adv_NoPreferred"], row.AdviceCount),
            _ => string.Empty,
        };
    }
}

// Localized failover advice for export (current UI language), mirroring
// FailoverAdviceConverter exactly.
internal static class FailoverExportText
{
    internal static string For(FailoverRow row)
    {
        var loc = LocalizationService.Instance;
        return row.Advice switch
        {
            FailoverAdviceKind.Viable => loc["Fo_Viable"],
            FailoverAdviceKind.Tight => loc["Fo_Tight"],
            FailoverAdviceKind.Critical => loc["Fo_Critical"],
            FailoverAdviceKind.AssignedHigh => loc["Fo_AssignedHigh"],
            _ => string.Empty,
        };
    }
}

// Localized Advisor recommendation for export (current UI language),
// mirroring AdvisorRecommendationConverter semantics: Pressure keeps its own
// text; sensitive names override Info findings only.
internal static class AdvisorExportText
{
    internal static string For(AdvisorRow row)
    {
        var loc = LocalizationService.Instance;
        if (row.Rule == AdvisorRule.Pressure)
        {
            return loc["Diag_Pressure"];
        }

        if (row.IsSensitive)
        {
            return loc["Diag_Sensitive"];
        }

        return row.Rule switch
        {
            AdvisorRule.LargeReserve => loc["Diag_LargeReserve"],
            _ => loc["Diag_Reserve"],
        };
    }
}
