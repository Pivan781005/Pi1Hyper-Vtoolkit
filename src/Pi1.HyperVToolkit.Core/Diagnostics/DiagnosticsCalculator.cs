using System.Text.RegularExpressions;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Utilities;

namespace Pi1.HyperVToolkit.Core.Diagnostics;

// Pure Diagnostics calculators. Exact ports of Pi1.Diagnostics.psm1:
//   Show-PiAdvisor (AdvisorCalculator)
//   Show-PiCsvLowFree (CsvLowFreeCalculator)
//   Show-PiClusterResourcesNotOnline (ClusterResourceDiagnostics)
// Diagnostics is a CONSUMER: inputs are immutable snapshots already collected
// by earlier phases (VM base rows, CSV rows, cluster resources). No WMI/CIM,
// no MSCluster queries, no remediation. Strictly read-only.
public static class AdvisorCalculator
{
    /// <summary>
    /// Exact port of Show-PiAdvisor row selection and precedence:
    /// only State == "Running" rows are considered (PowerShell -eq is
    /// case-insensitive); WasteGB &lt; 0 wins over WasteGB &gt; 16 wins over
    /// WasteGB &gt; 8 (if/elseif chain, one finding per input row at most);
    /// a sensitive-workload name (SQL|VEEAM|EXCHANGE|FORTI|FAZ,
    /// case-insensitive substring) overrides the Info recommendation only.
    /// Output preserves input order (no Sort-Object in the reference) and
    /// emits one row per qualifying INPUT row (multi-NIC VMs yield one row
    /// per base row, exactly like PowerShell).
    /// </summary>
    public static IReadOnlyList<AdvisorRow> Build(IEnumerable<VirtualMachineRow> vmRows)
    {
        var rows = new List<AdvisorRow>();
        foreach (var vm in vmRows)
        {
            if (!string.Equals(vm.State, "Running", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? severity = null;
            AdvisorRule rule;
            if (vm.WasteGB < 0)
            {
                severity = "Warning";
                rule = AdvisorRule.Pressure;
            }
            else if (vm.WasteGB > Thresholds.WasteLargeInfoGb)
            {
                severity = "Info";
                rule = AdvisorRule.LargeReserve;
            }
            else if (vm.WasteGB > Thresholds.WasteInfoGb)
            {
                severity = "Info";
                rule = AdvisorRule.Reserve;
            }
            else
            {
                continue;
            }

            var sensitive = IsSensitiveWorkload(vm.VM);
            rows.Add(new AdvisorRow(
                severity,
                vm.HostNode,
                vm.VM,
                vm.AssignedGB,
                vm.DemandGB,
                vm.WasteGB,
                rule,
                sensitive));
        }

        return rows;
    }

    /// <summary>
    /// PowerShell: $vm.VM -match "SQL|VEEAM|EXCHANGE|FORTI|FAZ"
    /// (-match is case-insensitive substring match).
    /// </summary>
    public static bool IsSensitiveWorkload(string? vmName)
    {
        if (string.IsNullOrEmpty(vmName))
        {
            return false;
        }

        return Regex.IsMatch(
            vmName,
            Thresholds.SensitiveWorkloadPattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// Reference-equivalent Slovak recommendation prose for parity/text
    /// comparisons. WPF renders via localized keys; tests may use this to
    /// assert semantic output without polluting Core with UI language.
    /// </summary>
    public static string ReferenceRecommendation(AdvisorRow row)
    {
        if (string.Equals(row.Severity, "Warning", StringComparison.Ordinal) &&
            row.Rule == AdvisorRule.Pressure)
        {
            return "Demand je vyšší ako Assigned. Sledovať RAM / zvážiť navýšenie.";
        }

        if (row.IsSensitive)
        {
            return "Špecifický workload. Neznižovať bez dlhšieho sledovania.";
        }

        return row.Rule switch
        {
            AdvisorRule.LargeReserve => "Veľká RAM rezerva. Kandidát na zníženie po sledovaní.",
            AdvisorRule.Reserve => "RAM rezerva > 8 GB. Sledovať.",
            _ => "Demand je vyšší ako Assigned. Sledovať RAM / zvážiť navýšenie.",
        };
    }
}

/// <summary>
/// Exact port of Show-PiCsvLowFree: FreePercent &lt; 20, sorted by
/// FreePercent ascending. Intentionally DIFFERENT from the Dashboard /
/// Health Score 15 % rule (Thresholds.CsvWarningPercent) — do not unify.
/// </summary>
public static class CsvLowFreeCalculator
{
    public static IReadOnlyList<CsvRow> Filter(IEnumerable<CsvRow> csvRows) =>
        csvRows
            .Where(c => c.FreePercent < Thresholds.CsvLowFreeViewPercent)
            .OrderBy(c => c.FreePercent)
            .ToList();
}

/// <summary>
/// Exact port of Show-PiClusterResourcesNotOnline: State -ne "Online"
/// (Offline IS included — unlike Health Score which tolerates Offline),
/// no resource-type exclusions, sorted by State, OwnerGroup, Name.
/// </summary>
public static class ClusterResourceDiagnostics
{
    public static IReadOnlyList<ClusterResourceRow> FilterNotOnline(
        IEnumerable<ClusterResourceRow> resources) =>
        resources
            .Where(r => !string.Equals(r.State, "Online", StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.State, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.OwnerGroup, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
