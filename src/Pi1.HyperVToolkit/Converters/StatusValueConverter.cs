using System.Globalization;
using System.Windows.Data;
using Pi1.HyperVToolkit.Core.State;

namespace Pi1.HyperVToolkit.Converters;

// Localizes canonical backend status/enum values for DISPLAY ONLY.
// Unknown values pass through unchanged; Core models are never mutated.
public sealed class StatusValueConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var canonical = value?.ToString();
        if (string.IsNullOrEmpty(canonical))
        {
            return string.Empty;
        }

        // "Running"/"Off" exist both as VM states and as VM-summary metrics
        // with different translations; ConverterParameter=Metric selects the
        // metric domain. StateFilter additionally maps the "All" filter value.
        if (string.Equals(parameter as string, "Metric", StringComparison.Ordinal))
        {
            if (string.Equals(canonical, "Running", StringComparison.Ordinal))
            {
                return LocalizationService.Instance["M_Running"];
            }

            if (string.Equals(canonical, "Off", StringComparison.Ordinal))
            {
                return LocalizationService.Instance["M_Off"];
            }
        }

        if (string.Equals(parameter as string, "StateFilter", StringComparison.Ordinal) &&
            string.Equals(canonical, "All", StringComparison.Ordinal))
        {
            return LocalizationService.Instance["Opt_All"];
        }

        return LocalizationService.Instance[StatusKey(canonical)];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    internal static string StatusKey(string canonical) => canonical switch
    {
        "Running" => "V_Running",
        "Off" => "V_Off",
        "Healthy" => "V_Healthy",
        "Warning" => "V_Warning",
        "Unhealthy" => "V_Unhealthy",
        "Unknown" => "V_Unknown",
        "None" => "V_None",
        "OK" => "V_OK",
        "Info" => "V_Info",
        "N/A" => "V_NA",
        "Internal" => "V_Internal",
        "External" => "V_External",
        "Private" => "V_Private",
        "Untagged" => "V_Untagged",
        "Access" => "V_Access",
        "Trunk" => "V_Trunk",
        "Thin" => "V_Thin",
        "Fixed" => "V_Fixed",
        "Dynamic" => "V_Dynamic",
        "Differencing" => "V_Differencing",
        "New" => "V_New",
        "Starting" => "V_Starting",
        "Stopping" => "V_Stopping",
        "Stopped" => "V_Stopped",
        "Suspended" => "V_Suspended",
        "Completed" => "V_Completed",
        "Paused" => "V_Paused",
        "Saved" => "V_Saved",
        "True" => "V_Yes",
        "False" => "V_No",
        "Up" => "V_Up",
        "Down" => "V_Down",
        "Joining" => "V_Joining",
        "Online" => "V_Online",
        "Offline" => "V_Offline",
        "Failed" => "V_Failed",
        "Pending" => "V_Pending",
        "PartialOnline" => "V_PartialOnline",
        "OnlinePending" => "V_OnlinePending",
        "OfflinePending" => "V_OfflinePending",
        "Inherited" => "V_Inherited",
        "Initializing" => "V_Initializing",
        "Critical" => "V_Critical",
        "Unavailable" => "V_Unavailable",
        "Partitioned" => "V_Partitioned",
        "Resources" => "A_Resources",
        "CSV Capacity" => "A_CsvCapacity",
        "Quorum/Witness" => "A_Quorum",
        "VM Memory Pressure" => "A_MemoryPressure",
        "Cluster" => "A_Cluster",
        "Scope" => "A_Scope",
        "Nodes" => "A_Nodes",
        "VM Running" => "A_VMRunning",
        "VM Off" => "A_VMOff",
        "RAM Total" => "A_RamTotal",
        "RAM Used" => "A_RamUsed",
        "RAM Free" => "A_RamFree",
        "CSV" => "A_CSV",
        "Storage Jobs" => "A_StorageJobs",
        "Warnings" => "A_Warnings",
        "Total VM" => "M_TotalVM",
        "AssignedGB Running" => "M_AssignedRunning",
        "DemandGB Running" => "M_DemandRunning",
        "WasteGB Running" => "M_WasteRunning",
        "Storage Pools" => "S_Pools",
        "Virtual Disks" => "S_VDisks",
        _ => canonical,
    };
}
