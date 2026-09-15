using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Infrastructure.Cim;
using Pi1.HyperVToolkit.Infrastructure.HyperV;
using Pi1.HyperVToolkit.Infrastructure.SystemInfo;
using Xunit.Abstractions;

namespace Pi1.HyperVToolkit.Tests.Parity;

/// <summary>
/// Mandatory parity gates: C# collectors vs the reference PowerShell collectors
/// on the SAME machine and scope (Local). Any DIFF failure must be investigated,
/// not adjusted away. Where the environment prevents comparison, the test
/// reports NOT TESTABLE IN CURRENT ENVIRONMENT and passes.
/// </summary>
public sealed class CollectorParityTests : ParityTestBase
{
    private static readonly TimeSpan ReferenceTimeout = TimeSpan.FromSeconds(90);

    public CollectorParityTests(ITestOutputHelper output)
        : base(output)
    {
    }

    [Fact]
    public async Task VmBaseParity_LocalScope()
    {
        var reference = await PowerShellReference.CollectAsync("Get-PiVMBaseRows", ReferenceTimeout);
        if (!reference.IsAvailable)
        {
            ReportNotTestable(Output, reference.Reason);
            return;
        }

        var psRows = ParityRow.ParseRows(reference.Json,
            ["CPU", "AssignedGB", "DemandGB", "WasteGB", "StartupGB", "MinimumGB", "MaximumGB"]);
        Output.WriteLine($"PS rows: {psRows.Count}");

        var service = new HyperVService(
            new MmiCimQuerier(NullLogger<MmiCimQuerier>.Instance),
            new VhdInspector(
                new MmiCimQuerier(NullLogger<MmiCimQuerier>.Instance),
                NullLogger<VhdInspector>.Instance),
            NullLogger<HyperVService>.Instance);
        var results = await service.GetVirtualMachinesAsync([Environment.MachineName]);
        var failures = results.Where(r => !r.IsSuccess).ToList();
        foreach (var failure in failures)
        {
            Output.WriteLine($"C# node warning: {failure.Error?.Message} {failure.Error?.Detail}");
        }

        var csRows = results.Where(r => r.IsSuccess).SelectMany(r => r.Data!).ToList();
        Output.WriteLine($"C# rows: {csRows.Count}");

        var csJson = JsonSerializer.Serialize(csRows.Select(r => new Dictionary<string, object?>
        {
            ["HostNode"] = r.HostNode,
            ["VM"] = r.VM,
            ["State"] = r.State,
            ["CPU"] = r.CPU,
            ["AssignedGB"] = r.AssignedGB,
            ["DemandGB"] = r.DemandGB,
            ["WasteGB"] = r.WasteGB,
            ["IPv4"] = r.IPv4,
            ["MAC"] = r.MAC,
            ["Switch"] = r.Switch,
            ["Uptime"] = r.UptimeText,
            ["Dynamic"] = r.Dynamic,
            ["StartupGB"] = r.StartupGB,
            ["MinimumGB"] = r.MinimumGB,
            ["MaximumGB"] = r.MaximumGB,
        }));
        var csParsed = ParityRow.ParseRows(csJson, ["CPU", "AssignedGB", "DemandGB", "WasteGB", "StartupGB", "MinimumGB", "MaximumGB"]);

        Func<Dictionary<string, string>, string> vmKey =
            r => $"{r.GetValueOrDefault("HostNode")}|{r.GetValueOrDefault("VM")}|{r.GetValueOrDefault("MAC")}";
        var diffs = ParityRow.Diff(
            psRows, csParsed,
            vmKey,
            ["HostNode", "VM", "State", "CPU", "AssignedGB", "DemandGB", "WasteGB", "IPv4", "MAC", "Switch", "Dynamic", "StartupGB", "MinimumGB", "MaximumGB"],
            TolerantNumericEquals);

        // Uptime is a live value sampled at different moments by each
        // collector: accept at most a one-minute boundary crossing, compare
        // everything else exactly (malformed values fall back to exact text).
        diffs.AddRange(DiffUptime(psRows, csParsed, vmKey));

        foreach (var diff in diffs)
        {
            Output.WriteLine(diff);
        }

        Assert.True(diffs.Count == 0, $"VM base parity failed with {diffs.Count} difference(s). See test output.");
    }

    private List<string> DiffUptime(
        List<Dictionary<string, string>> expected,
        List<Dictionary<string, string>> actual,
        Func<Dictionary<string, string>, string> key)
    {
        var diffs = new List<string>();
        var actualByKey = actual
            .GroupBy(key)
            .ToDictionary(g => g.Key, g => new Queue<Dictionary<string, string>>(g), StringComparer.OrdinalIgnoreCase);

        foreach (var exp in expected)
        {
            var k = key(exp);
            if (!actualByKey.TryGetValue(k, out var queue) || queue.Count == 0)
            {
                continue; // missing-row diff already reported by ParityRow.Diff.
            }

            var act = queue.Dequeue();
            exp.TryGetValue("Uptime", out var e);
            act.TryGetValue("Uptime", out var a);
            if (ParityUptime.WithinTolerance(e, a, toleranceMinutes: 1))
            {
                if (!string.Equals((e ?? string.Empty).Trim(), (a ?? string.Empty).Trim(), StringComparison.Ordinal))
                {
                    Output.WriteLine($"TOLERATED Uptime: PS='{e}' C#='{a}' (live counter drift).");
                }

                continue;
            }

            diffs.Add($"DIFF row {k} field Uptime: PS='{e}' C#='{a}'");
        }

        return diffs;
    }

    [Fact]
    public async Task VmNetworkParity_LocalScope()
    {
        var reference = await PowerShellReference.CollectAsync("Get-PiVMNetworkRows", ReferenceTimeout);
        if (!reference.IsAvailable)
        {
            ReportNotTestable(Output, reference.Reason);
            return;
        }

        var psRows = ParityRow.ParseRows(reference.Json, []);
        var service = new HyperVService(
            new MmiCimQuerier(NullLogger<MmiCimQuerier>.Instance),
            new VhdInspector(
                new MmiCimQuerier(NullLogger<MmiCimQuerier>.Instance),
                NullLogger<VhdInspector>.Instance),
            NullLogger<HyperVService>.Instance);
        var results = await service.GetVmNetworkAdaptersAsync([Environment.MachineName]);
        var csRows = results.Where(r => r.IsSuccess).SelectMany(r => r.Data!).ToList();

        var csJson = JsonSerializer.Serialize(csRows.Select(r => new Dictionary<string, object?>
        {
            ["HostNode"] = r.HostNode,
            ["VMName"] = r.VMName,
            ["IPv4"] = r.IPv4,
            ["MacAddress"] = r.MacAddress,
            ["SwitchName"] = r.SwitchName,
            ["AllIPs"] = r.AllIPs,
        }));
        var diffs = ParityRow.Diff(
            psRows, ParityRow.ParseRows(csJson, []),
            r => $"{r.GetValueOrDefault("HostNode")}|{r.GetValueOrDefault("VMName")}|{r.GetValueOrDefault("MacAddress")}",
            ["HostNode", "VMName", "IPv4", "MacAddress", "SwitchName", "AllIPs"]);

        foreach (var diff in diffs)
        {
            Output.WriteLine(diff);
        }

        Assert.True(diffs.Count == 0, $"VM network parity failed with {diffs.Count} difference(s). See test output.");
    }

    [Fact]
    public async Task NodeHardwareParity_LocalScope()
    {
        var reference = await PowerShellReference.CollectAsync("Get-PiNodeHardwareRows", ReferenceTimeout);
        if (!reference.IsAvailable)
        {
            ReportNotTestable(Output, reference.Reason);
            return;
        }

        var psRows = ParityRow.ParseRows(reference.Json,
            ["Sockets", "Cores", "LogicalCPU", "RAMGB", "UsedRAMGB", "FreeRAMGB", "RAMUsedPct"]);
        var psRow = Assert.Single(psRows);

        if (IsDegradedPsHardwareRow(psRow) && !await IsWinRmRunningAsync())
        {
            // The reference collector degrades to an all-empty row when remote
            // CIM/WSMan is unavailable (its own Invoke-PiSafe path) — WinRM is
            // stopped here, so there is no reference data to compare against.
            // The C# collector intentionally does not need WinRM for the local
            // machine (local CimSession) and therefore still returns real data.
            ReportNotTestable(Output, "NOT TESTABLE IN CURRENT ENVIRONMENT: WinRM is stopped, " +
                "so the PowerShell reference degrades to an empty hardware row. " +
                "C# local queries do not require WinRM by design (deliberate improvement).");
            return;
        }

        var service = new SystemInformationService(
            new MmiCimQuerier(NullLogger<MmiCimQuerier>.Instance), NullLogger<SystemInformationService>.Instance);
        var results = await service.GetNodeHardwareAsync([Environment.MachineName]);
        var ok = Assert.Single(results, r => r.IsSuccess);
        Assert.NotNull(ok.Data);
        var hw = ok.Data!;

        var diffs = new List<string>();
        Check(diffs, "Node", psRow.GetValueOrDefault("Node"), hw.Node);
        Check(diffs, "Manufacturer", psRow.GetValueOrDefault("Manufacturer"), hw.Manufacturer);
        Check(diffs, "Model", psRow.GetValueOrDefault("Model"), hw.Model);
        Check(diffs, "OS", psRow.GetValueOrDefault("OS"), hw.OS);
        Check(diffs, "Version", psRow.GetValueOrDefault("Version"), hw.Version);
        CheckInt(diffs, "Sockets", psRow.GetValueOrDefault("Sockets"), hw.Sockets);
        CheckInt(diffs, "Cores", psRow.GetValueOrDefault("Cores"), hw.Cores);
        CheckInt(diffs, "LogicalCPU", psRow.GetValueOrDefault("LogicalCPU"), hw.LogicalCPU);
        CheckNumeric(diffs, "RAMGB", psRow.GetValueOrDefault("RAMGB"), hw.RAMGB);
        // VOLATILE live counters sampled seconds apart (same TOLERATED policy
        // as HostSettingsParity): Used/FreeRAMGB ±0.5 GB, RAMUsedPct ±1.0 pt.
        // Everything else above stays exact.
        CheckLiveNumeric(diffs, Output, "UsedRAMGB", psRow.GetValueOrDefault("UsedRAMGB"), hw.UsedRAMGB, tolerance: 0.5);
        CheckLiveNumeric(diffs, Output, "FreeRAMGB", psRow.GetValueOrDefault("FreeRAMGB"), hw.FreeRAMGB, tolerance: 0.5);
        CheckLiveNumeric(diffs, Output, "RAMUsedPct", psRow.GetValueOrDefault("RAMUsedPct"), hw.RAMUsedPct, tolerance: 1.0);

        // CPU names: same set, order-insensitive (both enumerate Win32_Processor).
        var psCpu = (psRow.GetValueOrDefault("CPUName") ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(s => s).ToList();
        var csCpu = hw.CPUName
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(s => s).ToList();
        if (!psCpu.SequenceEqual(csCpu, StringComparer.Ordinal))
        {
            diffs.Add($"DIFF CPUName: PS='{psRow.GetValueOrDefault("CPUName")}' C#='{hw.CPUName}'");
        }

        // Uptime: both sides derive from the same boot time seconds apart; allow 2 minutes.
        var psMinutes = ParseUptimeMinutes(psRow.GetValueOrDefault("Uptime"));
        var csMinutes = hw.Uptime.HasValue ? (int)hw.Uptime.Value.TotalMinutes : -1;
        if (psMinutes < 0 || csMinutes < 0 || Math.Abs(psMinutes - csMinutes) > 2)
        {
            diffs.Add($"DIFF Uptime: PS='{psRow.GetValueOrDefault("Uptime")}' C#='{hw.UptimeText}'");
        }

        foreach (var diff in diffs)
        {
            Output.WriteLine(diff);
        }

        Assert.True(diffs.Count == 0, $"Node hardware parity failed with {diffs.Count} difference(s). See test output.");
    }

    [Fact]
    public async Task HostSettingsParity_LocalScope()
    {
        var reference = await PowerShellReference.CollectAsync("Get-PiVMHostSettingsRows", ReferenceTimeout);
        if (!reference.IsAvailable)
        {
            ReportNotTestable(Output, reference.Reason);
            return;
        }

        var psRows = ParityRow.ParseRows(reference.Json, ["CPU", "RAMGB", "FreeRAMGB", "RAMUsedPct"]);

        var hwService = new SystemInformationService(
            new MmiCimQuerier(NullLogger<MmiCimQuerier>.Instance), NullLogger<SystemInformationService>.Instance);
        var hvService = new HyperVService(
            new MmiCimQuerier(NullLogger<MmiCimQuerier>.Instance),
            new VhdInspector(
                new MmiCimQuerier(NullLogger<MmiCimQuerier>.Instance),
                NullLogger<VhdInspector>.Instance),
            NullLogger<HyperVService>.Instance);
        var hwResults = await hwService.GetNodeHardwareAsync([Environment.MachineName]);
        var hwByNode = hwResults
            .Where(r => r.IsSuccess && r.Data is not null)
            .ToDictionary(r => r.Node, r => r.Data, StringComparer.OrdinalIgnoreCase);
        var results = await hvService.GetHostSettingsAsync([Environment.MachineName], hwByNode);

        if (psRows.Count == 0)
        {
            // PS found no Hyper-V host settings (Get-VMHost failed there).
            var csEmpty = results.All(r => !r.IsSuccess);
            Output.WriteLine(csEmpty
                ? "Both sides empty: parity holds (no Hyper-V host settings)."
                : "PS empty but C# returned rows — documented difference, investigate.");
            Assert.True(csEmpty, "PS has no host settings but C# does. See test output.");
            return;
        }

        var psRow = Assert.Single(psRows);
        var ok = Assert.Single(results, r => r.IsSuccess);
        Assert.NotNull(ok.Data);
        var hs = ok.Data!;

        var diffs = new List<string>();
        Check(diffs, "Node", psRow.GetValueOrDefault("Node"), hs.Node);
        CheckInt(diffs, "CPU", psRow.GetValueOrDefault("CPU"), hs.LogicalProcessorCount);
        CheckNumeric(diffs, "RAMGB", psRow.GetValueOrDefault("RAMGB"), hs.MemoryCapacityGB);
        CheckBool(diffs, "NUMA", psRow.GetValueOrDefault("NUMA"), hs.NumaSpanningEnabled);
        CheckBool(diffs, "Migration", psRow.GetValueOrDefault("Migration"), hs.MigrationEnabled);
        CheckInt(diffs, "MaxMig", psRow.GetValueOrDefault("MaxMig"), hs.MaxMigrations);
        CheckBool(diffs, "Enhanced", psRow.GetValueOrDefault("Enhanced"), hs.EnhancedSessionMode);

        // FreeRAMGB/RAMUsedPct come from the hardware join on both sides. When the
        // PS reference degrades (WinRM stopped) it reports empty strings while C#
        // still returns real data — an expected environmental difference, not a bug.
        var psFree = psRow.GetValueOrDefault("FreeRAMGB");
        var psPct = psRow.GetValueOrDefault("RAMUsedPct");
        if (string.IsNullOrEmpty(psFree) && string.IsNullOrEmpty(psPct) && !await IsWinRmRunningAsync())
        {
            Output.WriteLine("SKIPPED FreeRAMGB/RAMUsedPct: PS reference degraded (WinRM stopped); C# has real data by design.");
        }
        else
        {
            CheckLiveNumeric(diffs, Output, "FreeRAMGB", psFree, hs.FreeRAMGB, tolerance: 0.5);
            CheckLiveNumeric(diffs, Output, "RAMUsedPct", psPct, hs.RAMUsedPct, tolerance: 1.0);
        }
        if (!string.Equals(psRow.GetValueOrDefault("VMPath"), hs.VirtualMachinePath, StringComparison.OrdinalIgnoreCase))
        {
            diffs.Add($"DIFF VMPath: PS='{psRow.GetValueOrDefault("VMPath")}' C#='{hs.VirtualMachinePath}'");
        }

        if (!string.Equals(psRow.GetValueOrDefault("VHDPath"), hs.VirtualHardDiskPath, StringComparison.OrdinalIgnoreCase))
        {
            diffs.Add($"DIFF VHDPath: PS='{psRow.GetValueOrDefault("VHDPath")}' C#='{hs.VirtualHardDiskPath}'");
        }

        foreach (var diff in diffs)
        {
            Output.WriteLine(diff);
        }

        Assert.True(diffs.Count == 0, $"Host settings parity failed with {diffs.Count} difference(s). See test output.");
    }

    private static bool TolerantNumericEquals(string field, string expected, string actual)
    {
        if (ParityNumbers.Equals(expected, actual))
        {
            return true;
        }

        // GB values come from live counters seconds apart; allow 0.2 GB drift on
        // the volatile memory-usage fields only. Anything else must match exactly.
        if (field is "AssignedGB" or "DemandGB" or "WasteGB")
        {
            return ParityNumbers.Within(expected, actual, 0.2);
        }

        return false;
    }

    private static void Check(List<string> diffs, string field, string? expected, string actual)
    {
        if (!string.Equals(expected ?? string.Empty, actual, StringComparison.Ordinal))
        {
            diffs.Add($"DIFF {field}: PS='{expected}' C#='{actual}'");
        }
    }

    /// <summary>Exact numeric equality (8 == 8.0, 31.9 == 31.90); different values fail.</summary>
    private static void CheckNumeric(List<string> diffs, string field, string? expected, double? actual)
    {
        var actualText = actual.HasValue
            ? actual.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;
        if (!ParityNumbers.Equals(expected, actualText))
        {
            diffs.Add($"DIFF {field}: PS='{expected}' C#='{actualText}'");
        }
    }

    private static void CheckInt(List<string> diffs, string field, string? expected, int? actual)
    {
        CheckNumeric(diffs, field, expected, actual.HasValue ? (double)actual.Value : null);
    }

    private static void CheckBool(List<string> diffs, string field, string? expected, bool? actual)
    {
        var actualText = actual.HasValue ? (actual.Value ? "True" : "False") : string.Empty;
        Check(diffs, field, expected, actualText);
    }

    /// <summary>
    /// The PS Invoke-PiSafe degradation signature: every CIM query failed, so the
    /// row carries empty strings (and Sockets 0). Comparing against it is meaningless.
    /// </summary>
    private static bool IsDegradedPsHardwareRow(Dictionary<string, string> psRow) =>
        string.IsNullOrEmpty(psRow.GetValueOrDefault("Manufacturer")) &&
        string.IsNullOrEmpty(psRow.GetValueOrDefault("OS")) &&
        string.IsNullOrEmpty(psRow.GetValueOrDefault("RAMGB"));

    /// <summary>WinRM state via a local (WinRM-independent) CIM query.</summary>
    private static async Task<bool> IsWinRmRunningAsync()
    {
        try
        {
            using var querier = new MmiCimQuerier(NullLogger<MmiCimQuerier>.Instance);
            var rows = await querier.QueryAsync(
                ".", @"root\cimv2", "SELECT State FROM Win32_Service WHERE Name = 'WinRM'",
                TimeSpan.FromSeconds(15));
            return rows.Any(r =>
                string.Equals(
                    r.TryGetValue("State", out var state) ? Convert.ToString(state) : null,
                    "Running", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Live-counter comparison with an explicit tolerance (reported when used).
    /// </summary>
    private static void CheckLiveNumeric(
        List<string> diffs, ITestOutputHelper output, string field, string? expected, double? actual, double tolerance)
    {
        var actualText = actual.HasValue
            ? actual.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;
        if (string.Equals(expected ?? string.Empty, actualText, StringComparison.Ordinal))
        {
            return;
        }

        if (double.TryParse(expected, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var e)
            && actual.HasValue && Math.Abs(e - actual.Value) <= tolerance)
        {
            output.WriteLine($"TOLERATED {field}: PS='{expected}' C#='{actualText}' (live counter drift).");
            return;
        }

        diffs.Add($"DIFF {field}: PS='{expected}' C#='{actualText}'");
    }

    private static int ParseUptimeMinutes(string? text)
    {
        // "3d 4h 5m" | "2h 30m" | "45m" | "-" | "0m"
        if (string.IsNullOrWhiteSpace(text) || text == "-")
        {
            return -1;
        }

        try
        {
            var total = 0;
            foreach (var part in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.EndsWith('d') && int.TryParse(part[..^1], out var d))
                {
                    total += d * 24 * 60;
                }
                else if (part.EndsWith('h') && int.TryParse(part[..^1], out var h))
                {
                    total += h * 60;
                }
                else if (part.EndsWith('m') && int.TryParse(part[..^1], out var m))
                {
                    total += m;
                }
            }

            return total;
        }
        catch (Exception)
        {
            return -1;
        }
    }
}
