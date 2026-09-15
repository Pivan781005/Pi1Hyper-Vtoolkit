using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Pi1.HyperVToolkit.Infrastructure.Cim;
using Pi1.HyperVToolkit.Infrastructure.HyperV;
using Pi1.HyperVToolkit.Infrastructure.Storage;
using Xunit.Abstractions;

namespace Pi1.HyperVToolkit.Tests.Parity;

// Phase 4 storage parity: C# native collectors vs the reference PowerShell.
// Snippets replicate the Show-Pi* bodies verbatim (modules stay untouched);
// array fields (OperationalStatus) are joined with ", " on BOTH sides before
// comparison, matching console rendering without weakening values.
public sealed class StorageParityTests : ParityTestBase
{
    private static readonly TimeSpan ReferenceTimeout = TimeSpan.FromSeconds(90);
    private static readonly string[] Modules = ["Pi1.Core.psm1", "Pi1.Data.psm1", "Pi1.Storage.psm1"];

    public StorageParityTests(ITestOutputHelper output)
        : base(output)
    {
    }

    [Fact]
    public async Task StorageJobsParity_Local()
    {
        var reference = await PowerShellReference.CollectAsync("Get-PiStorageJobRows", ReferenceTimeout);
        if (!reference.IsAvailable)
        {
            ReportNotTestable(Output, reference.Reason);
            return;
        }

        var psRows = ParityRow.ParseRows(reference.Json, ["PercentComplete", "BytesProcessed", "BytesTotal"]);
        var cs = await new StorageService(
            new MmiCimQuerier(NullLogger<MmiCimQuerier>.Instance),
            NullLogger<StorageService>.Instance).GetStorageJobsAsync();
        Output.WriteLine($"PS jobs: {psRows.Count}, C# jobs: {cs.Count}");

        var csJson = JsonSerializer.Serialize(cs.Select(r => new Dictionary<string, object?>
        {
            ["Name"] = r.Name,
            ["JobState"] = r.JobState,
            ["JobType"] = r.JobType,
            ["PercentComplete"] = r.PercentComplete,
            ["BytesProcessed"] = r.BytesProcessed,
            ["BytesTotal"] = r.BytesTotal,
            ["ElapsedMinutes"] = r.ElapsedTime?.TotalMinutes,
        }));
        var diffs = ParityRow.Diff(
            NormalizeTimes(psRows), ParityRow.ParseRows(csJson, ["PercentComplete", "BytesProcessed", "BytesTotal", "ElapsedMinutes"]),
            r => $"{r.GetValueOrDefault("Name")}",
            ["Name", "JobState", "PercentComplete", "BytesProcessed", "BytesTotal"]);
        // ElapsedTime compared separately (TimeSpan object vs minutes).
        diffs.AddRange(DiffElapsedMinutes(reference.Json, cs));
        foreach (var diff in diffs)
        {
            Output.WriteLine(diff);
        }

        Assert.True(diffs.Count == 0, $"Storage Jobs parity failed with {diffs.Count} difference(s).");
    }

    [Fact]
    public async Task StoragePoolsParity_Local()
    {
        const string snippet = "Get-StoragePool -ErrorAction SilentlyContinue | Where-Object {$_.IsPrimordial -eq $false} | Select-Object FriendlyName, HealthStatus, OperationalStatus, @{Name='SizeGB';Expression={[math]::Round($_.Size/1GB,1)}}, @{Name='AllocatedGB';Expression={[math]::Round($_.AllocatedSize/1GB,1)}}, @{Name='FreeGB';Expression={[math]::Round(($_.Size-$_.AllocatedSize)/1GB,1)}} | Sort-Object FriendlyName";
        var reference = await PowerShellReference.CollectSnippetAsync(snippet, Modules, ReferenceTimeout);
        if (!reference.IsAvailable)
        {
            ReportNotTestable(Output, reference.Reason);
            return;
        }

        var psRows = ParityRow.ParseRows(reference.Json, ["SizeGB", "AllocatedGB", "FreeGB"]);
        var cs = await new StorageService(
            new MmiCimQuerier(NullLogger<MmiCimQuerier>.Instance),
            NullLogger<StorageService>.Instance).GetStoragePoolsAsync();
        Output.WriteLine($"PS pools: {psRows.Count}, C# pools: {cs.Count}");

        var csJson = JsonSerializer.Serialize(cs.Select(r => new Dictionary<string, object?>
        {
            ["FriendlyName"] = r.FriendlyName,
            ["HealthStatus"] = r.HealthStatus,
            ["OperationalStatus"] = r.OperationalStatus,
            ["SizeGB"] = r.SizeGB,
            ["AllocatedGB"] = r.AllocatedGB,
            ["FreeGB"] = r.FreeGB,
        }));
        var diffs = ParityRow.Diff(psRows, ParityRow.ParseRows(csJson, ["SizeGB", "AllocatedGB", "FreeGB"]),
            r => $"{r.GetValueOrDefault("FriendlyName")}",
            ["FriendlyName", "HealthStatus", "OperationalStatus", "SizeGB", "AllocatedGB", "FreeGB"]);
        foreach (var diff in diffs)
        {
            Output.WriteLine(diff);
        }

        Assert.True(diffs.Count == 0, $"Storage Pools parity failed with {diffs.Count} difference(s).");
    }

    [Fact]
    public async Task VirtualDisksParity_Local()
    {
        const string snippet = "Get-VirtualDisk -ErrorAction SilentlyContinue | Select-Object FriendlyName, HealthStatus, OperationalStatus, ResiliencySettingName, ProvisioningType, @{Name='SizeGB';Expression={[math]::Round($_.Size/1GB,1)}}, @{Name='AllocatedGB';Expression={[math]::Round($_.AllocatedSize/1GB,1)}} | Sort-Object FriendlyName";
        var reference = await PowerShellReference.CollectSnippetAsync(snippet, Modules, ReferenceTimeout);
        if (!reference.IsAvailable)
        {
            ReportNotTestable(Output, reference.Reason);
            return;
        }

        var psRows = ParityRow.ParseRows(reference.Json, ["SizeGB", "AllocatedGB"]);
        var cs = await new StorageService(
            new MmiCimQuerier(NullLogger<MmiCimQuerier>.Instance),
            NullLogger<StorageService>.Instance).GetVirtualDisksAsync();
        Output.WriteLine($"PS vdisks: {psRows.Count}, C# vdisks: {cs.Count}");

        var csJson = JsonSerializer.Serialize(cs.Select(r => new Dictionary<string, object?>
        {
            ["FriendlyName"] = r.FriendlyName,
            ["HealthStatus"] = r.HealthStatus,
            ["OperationalStatus"] = r.OperationalStatus,
            ["ResiliencySettingName"] = r.ResiliencySettingName,
            ["ProvisioningType"] = r.ProvisioningType,
            ["SizeGB"] = r.SizeGB,
            ["AllocatedGB"] = r.AllocatedGB,
        }));
        var diffs = ParityRow.Diff(psRows, ParityRow.ParseRows(csJson, ["SizeGB", "AllocatedGB"]),
            r => $"{r.GetValueOrDefault("FriendlyName")}",
            ["FriendlyName", "HealthStatus", "OperationalStatus", "ResiliencySettingName", "ProvisioningType", "SizeGB", "AllocatedGB"]);
        foreach (var diff in diffs)
        {
            Output.WriteLine(diff);
        }

        Assert.True(diffs.Count == 0, $"Virtual Disks parity failed with {diffs.Count} difference(s).");
    }

    [Fact]
    public async Task PhysicalDisksParity_Local()
    {
        const string snippet = "Get-PhysicalDisk -ErrorAction SilentlyContinue | Select-Object FriendlyName, SerialNumber, CanPool, HealthStatus, OperationalStatus, MediaType, BusType, @{Name='SizeGB';Expression={[math]::Round($_.Size/1GB,1)}}, Usage | Sort-Object MediaType, BusType, FriendlyName";
        var reference = await PowerShellReference.CollectSnippetAsync(snippet, Modules, ReferenceTimeout);
        if (!reference.IsAvailable)
        {
            ReportNotTestable(Output, reference.Reason);
            return;
        }

        var psRows = ParityRow.ParseRows(reference.Json, ["SizeGB"]);
        var cs = await new StorageService(
            new MmiCimQuerier(NullLogger<MmiCimQuerier>.Instance),
            NullLogger<StorageService>.Instance).GetPhysicalDisksAsync();
        Output.WriteLine($"PS disks: {psRows.Count}, C# disks: {cs.Count}");

        var csJson = JsonSerializer.Serialize(cs.Select(r => new Dictionary<string, object?>
        {
            ["FriendlyName"] = r.FriendlyName,
            ["MediaType"] = r.MediaType,
            ["BusType"] = r.BusType,
            ["SizeGB"] = r.SizeGB,
            ["HealthStatus"] = r.HealthStatus,
            ["OperationalStatus"] = r.OperationalStatus,
            ["CanPool"] = r.CanPool,
            ["Usage"] = r.Usage,
            ["SerialNumber"] = r.SerialNumber,
        }));
        var diffs = ParityRow.Diff(psRows, ParityRow.ParseRows(csJson, ["SizeGB"]),
            r => $"{r.GetValueOrDefault("FriendlyName")}|{r.GetValueOrDefault("SerialNumber")}",
            ["FriendlyName", "MediaType", "BusType", "SizeGB", "HealthStatus", "OperationalStatus", "CanPool", "Usage", "SerialNumber"]);
        foreach (var diff in diffs)
        {
            Output.WriteLine(diff);
        }

        Assert.True(diffs.Count == 0, $"Physical Disks parity failed with {diffs.Count} difference(s).");
    }

    [Fact]
    public async Task VolumesParity_Local()
    {
        // UniqueId/Path are test-only identity (two hidden volumes share the
        // displayed DriveLetter|FileSystemLabel key); compared columns stay
        // the original user-visible eight.
        const string snippet = "Get-Volume -ErrorAction SilentlyContinue | Where-Object {$_.DriveType -eq 'Fixed'} | Select-Object DriveLetter, FileSystemLabel, FileSystem, HealthStatus, OperationalStatus, UniqueId, Path, @{Name='SizeGB';Expression={[math]::Round($_.Size/1GB,1)}}, @{Name='FreeGB';Expression={[math]::Round($_.SizeRemaining/1GB,1)}}, @{Name='FreePercent';Expression={if($_.Size -gt 0){[math]::Round(($_.SizeRemaining/$_.Size)*100,1)}else{0}}} | Sort-Object DriveLetter, FileSystemLabel";
        var reference = await PowerShellReference.CollectSnippetAsync(snippet, Modules, ReferenceTimeout);
        if (!reference.IsAvailable)
        {
            ReportNotTestable(Output, reference.Reason);
            return;
        }

        var psRows = ParityRow.ParseRows(reference.Json, ["SizeGB", "FreeGB", "FreePercent"]);
        var cs = await new StorageService(
            new MmiCimQuerier(NullLogger<MmiCimQuerier>.Instance),
            NullLogger<StorageService>.Instance).GetVolumesAsync();
        Output.WriteLine($"PS volumes: {psRows.Count}, C# volumes: {cs.Count}");

        var csJson = JsonSerializer.Serialize(cs.Select(r => new Dictionary<string, object?>
        {
            ["DriveLetter"] = r.DriveLetter,
            ["FileSystemLabel"] = r.FileSystemLabel,
            ["FileSystem"] = r.FileSystem,
            ["HealthStatus"] = r.HealthStatus,
            ["OperationalStatus"] = r.OperationalStatus,
            ["UniqueId"] = r.UniqueId,
            ["Path"] = r.Path,
            ["SizeGB"] = r.SizeGB,
            ["FreeGB"] = r.FreeGB,
            ["FreePercent"] = r.FreePercent,
        }));
        var diffs = ParityRow.Diff(psRows, ParityRow.ParseRows(csJson, ["SizeGB", "FreeGB", "FreePercent"]),
            r => StableVolumeKey(r),
            ["DriveLetter", "FileSystemLabel", "FileSystem", "HealthStatus", "OperationalStatus", "SizeGB", "FreeGB", "FreePercent"],
            (field, e, a) => field is "SizeGB" or "FreeGB"
                ? ParityNumbers.Within(e, a, 0.2) // live counters drift between runs.
                : ParityNumbers.Equals(e, a));
        foreach (var diff in diffs)
        {
            Output.WriteLine(diff);
        }

        Assert.True(diffs.Count == 0, $"Volumes parity failed with {diffs.Count} difference(s).");
    }

    [Fact]
    public async Task CsvParity_Local()
    {
        var reference = await PowerShellReference.CollectAsync("Get-PiCSVRows", ReferenceTimeout);
        if (!reference.IsAvailable)
        {
            ReportNotTestable(Output, reference.Reason);
            return;
        }

        var psRows = ParityRow.ParseRows(reference.Json,
            ["SizeGB", "FreeGB", "UsedGB", "FreePercent"]);
        var cs = await new StorageService(
            new MmiCimQuerier(NullLogger<MmiCimQuerier>.Instance),
            NullLogger<StorageService>.Instance).GetCsvRowsAsync();
        Output.WriteLine($"PS CSV: {psRows.Count}, C# CSV: {cs.Count}");

        if (psRows.Count == 0 && cs.Count == 0)
        {
            Output.WriteLine("Both sides empty (no cluster capability here): parity holds vacuously.");
            return;
        }

        var csJson = JsonSerializer.Serialize(cs.Select(r => new Dictionary<string, object?>
        {
            ["Name"] = r.Name,
            ["State"] = r.State,
            ["OwnerNode"] = r.OwnerNode,
            ["SizeGB"] = r.SizeGB,
            ["FreeGB"] = r.FreeGB,
            ["UsedGB"] = r.UsedGB,
            ["FreePercent"] = r.FreePercent,
            ["Path"] = r.Path,
        }));
        var diffs = ParityRow.Diff(psRows, ParityRow.ParseRows(csJson, ["SizeGB", "FreeGB", "UsedGB", "FreePercent"]),
            r => $"{r.GetValueOrDefault("Name")}|{r.GetValueOrDefault("Path")}",
            ["Name", "State", "OwnerNode", "SizeGB", "FreeGB", "UsedGB", "FreePercent", "Path"]);
        foreach (var diff in diffs)
        {
            Output.WriteLine(diff);
        }

        Assert.True(diffs.Count == 0, $"CSV parity failed with {diffs.Count} difference(s).");
    }

    [Fact]
    public async Task VmStorageParity_Local()
    {
        var reference = await PowerShellReference.CollectAsync("Get-PiVMStorageRows", ReferenceTimeout);
        if (!reference.IsAvailable)
        {
            ReportNotTestable(Output, reference.Reason);
            return;
        }

        var psRows = ParityRow.ParseRows(reference.Json, ["VHDSizeGB", "VHDFileGB", "CSVFreeGB", "CSVFreePercent"]);
        var storage = new StorageService(
            new MmiCimQuerier(NullLogger<MmiCimQuerier>.Instance),
            NullLogger<StorageService>.Instance);
        var csvs = await storage.GetCsvRowsAsync();
        var querier = new MmiCimQuerier(NullLogger<MmiCimQuerier>.Instance);
        var service = new HyperVService(
            querier,
            new VhdInspector(querier, NullLogger<VhdInspector>.Instance),
            NullLogger<HyperVService>.Instance);
        var results = await service.GetVmStorageAsync([Environment.MachineName], csvs);
        var csRows = results.Where(r => r.IsSuccess).SelectMany(r => r.Data!).ToList();
        Output.WriteLine($"PS storage rows: {psRows.Count}, C# storage rows: {csRows.Count}");

        if (psRows.Count == 0 && csRows.Count == 0)
        {
            Output.WriteLine("Both sides empty (Hyper-V unreadable without elevation): parity holds vacuously.");
            return;
        }

        var csJson = JsonSerializer.Serialize(csRows.Select(r => new Dictionary<string, object?>
        {
            ["HostNode"] = r.HostNode,
            ["VM"] = r.VM,
            ["State"] = r.State,
            ["Controller"] = r.Controller,
            ["VHDFormat"] = r.VHDFormat,
            ["VHDType"] = r.VHDType,
            ["VHDSizeGB"] = r.VHDSizeGB,
            ["VHDFileGB"] = r.VHDFileGB,
            ["CSV"] = r.CSV,
            ["CSVFreeGB"] = r.CSVFreeGB,
            ["CSVFreePercent"] = r.CSVFreePercent,
            ["Path"] = r.Path,
        }));
        var diffs = ParityRow.Diff(psRows, ParityRow.ParseRows(csJson, ["VHDSizeGB", "VHDFileGB", "CSVFreeGB", "CSVFreePercent"]),
            r => $"{r.GetValueOrDefault("HostNode")}|{r.GetValueOrDefault("VM")}|{r.GetValueOrDefault("Path")}",
            ["HostNode", "VM", "State", "Controller", "VHDFormat", "VHDType", "VHDSizeGB", "VHDFileGB", "CSV", "CSVFreeGB", "CSVFreePercent", "Path"]);
        foreach (var diff in diffs)
        {
            Output.WriteLine(diff);
        }

        Assert.True(diffs.Count == 0, $"VM Storage parity failed with {diffs.Count} difference(s).");
    }

    private static List<Dictionary<string, string>> NormalizeTimes(List<Dictionary<string, string>> rows) => rows;

    // Stable test-only identity: UniqueId, then Path, then the displayed key.
    // Fixes the fake cross-diffs between the two hidden volumes that share
    // DriveLetter="" and FileSystemLabel="".
    internal static string StableVolumeKey(Dictionary<string, string> row)
    {
        var unique = row.GetValueOrDefault("UniqueId");
        if (!string.IsNullOrEmpty(unique))
        {
            return "UID:" + unique;
        }

        var path = row.GetValueOrDefault("Path");
        if (!string.IsNullOrEmpty(path))
        {
            return "PATH:" + path;
        }

        return $"{row.GetValueOrDefault("DriveLetter")}|{row.GetValueOrDefault("FileSystemLabel")}";
    }

    private static List<string> DiffElapsedMinutes(
        string psJson,
        IReadOnlyList<Core.Models.StorageJobRow> csRows)
    {
        var diffs = new List<string>();
        var psMinutes = ExtractElapsedMinutes(psJson);
        var csByName = csRows.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, minutes) in psMinutes)
        {
            if (!csByName.TryGetValue(name, out var cs))
            {
                continue; // missing-row diff already reported.
            }

            var csMinutes = cs.ElapsedTime?.TotalMinutes ?? double.NaN;
            if (double.IsNaN(csMinutes) || double.IsNaN(minutes) || Math.Abs(csMinutes - minutes) > 1.0)
            {
                diffs.Add($"DIFF job {name} ElapsedMinutes: PS='{minutes}' C#='{csMinutes}'");
            }
        }

        return diffs;
    }

    private static List<(string Name, double Minutes)> ExtractElapsedMinutes(string json)
    {
        var result = new List<(string, double)>();
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "null")
        {
            return result;
        }

        using var document = JsonDocument.Parse(json);
        var elements = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray()
            : Enumerable.Repeat(document.RootElement, 1);
        foreach (var element in elements)
        {
            var name = element.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;
            if (!element.TryGetProperty("ElapsedTime", out var elapsed))
            {
                continue;
            }

            double minutes = double.NaN;
            if (elapsed.ValueKind == JsonValueKind.Object && elapsed.TryGetProperty("TotalMinutes", out var total))
            {
                minutes = total.GetDouble();
            }
            else if (elapsed.ValueKind == JsonValueKind.String &&
                TimeSpan.TryParse(elapsed.GetString(), out var span))
            {
                minutes = span.TotalMinutes;
            }

            result.Add((name, minutes));
        }

        return result;
    }
}
