using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Pi1.HyperVToolkit.Infrastructure.Cim;
using Pi1.HyperVToolkit.Infrastructure.HyperV;
using Xunit.Abstractions;

namespace Pi1.HyperVToolkit.Tests.Parity;

// Phase 4 networking parity: virtual switches + adapter VLAN vs reference.
// Snippets mirror Show-PiVMSwitches / Show-PiVMAdapterVlan loop bodies for the
// local node (reference modules stay untouched). Enum values cast to string
// (console rendering parity); array VLAN lists joined before comparison.
public sealed class NetworkParityTests : ParityTestBase
{
    private static readonly TimeSpan ReferenceTimeout = TimeSpan.FromSeconds(90);
    private static readonly string[] Modules = ["Pi1.Core.psm1", "Pi1.Data.psm1", "Pi1.Networking.psm1"];

    // Canonical VLAN adapter identity (shared with VlanReferenceCanonicalizationTests).
    // The VLAN cmdlet on this Hyper-V version exposes identity through ParentAdapter
    // while $v.VMName / $v.VMNetworkAdapterName come back blank (original script
    // presentation bug — reference modules stay untouched, C# keeps the CORRECT
    // DOS 6.22 / Network Adapter identity). Default VM parameter set only.
    internal const string VlanIdentityCanonicalizer = """
        function Get-PiVlanReferenceIdentity {
            param($Vlan)
            $vmName = ""
            $adapterName = ""
            $props = $Vlan.PSObject.Properties
            $pVm = $props['VMName']
            if ($pVm -and $pVm.Value) { $vmName = [string]$pVm.Value }
            $pAd = $props['VMNetworkAdapterName']
            if ($pAd -and $pAd.Value) { $adapterName = [string]$pAd.Value }
            $pParent = $props['ParentAdapter']
            if (([string]::IsNullOrEmpty($vmName) -or [string]::IsNullOrEmpty($adapterName)) -and $pParent -and $pParent.Value) {
                $parentProps = $pParent.Value.PSObject.Properties
                if ([string]::IsNullOrEmpty($vmName)) {
                    $qVm = $parentProps['VMName']
                    if ($qVm -and $qVm.Value) { $vmName = [string]$qVm.Value }
                }
                if ([string]::IsNullOrEmpty($adapterName)) {
                    $qName = $parentProps['Name']
                    if ($qName -and $qName.Value) { $adapterName = [string]$qName.Value }
                }
            }
            return [PSCustomObject]@{ VMName = $vmName; VMNetworkAdapterName = $adapterName }
        }
        """;

    public NetworkParityTests(ITestOutputHelper output)
        : base(output)
    {
    }

    [Fact]
    public async Task VirtualSwitchParity_Local()
    {
        const string snippet = "$node=$env:COMPUTERNAME; $rows=@(); $switches=Get-VMSwitch -ComputerName $node -ErrorAction SilentlyContinue; foreach($sw in @($switches)){ $rows += [PSCustomObject]@{HostNode=$node; Name=$sw.Name; SwitchType=[string]$sw.SwitchType; NetAdapterInterfaceDescription=$sw.NetAdapterInterfaceDescription; AllowManagementOS=$sw.AllowManagementOS} }; $rows";
        var reference = await PowerShellReference.CollectSnippetAsync(snippet, Modules, ReferenceTimeout);
        if (!reference.IsAvailable)
        {
            ReportNotTestable(Output, reference.Reason);
            return;
        }

        var psRows = ParityRow.ParseRows(reference.Json, []);
        var querier = new MmiCimQuerier(NullLogger<MmiCimQuerier>.Instance);
        var service = new HyperVService(
            querier,
            new VhdInspector(querier, NullLogger<VhdInspector>.Instance),
            NullLogger<HyperVService>.Instance);
        var results = await service.GetVirtualSwitchesAsync([Environment.MachineName]);
        var csRows = results.Where(r => r.IsSuccess).SelectMany(r => r.Data!).ToList();
        Output.WriteLine($"PS switches: {psRows.Count}, C# switches: {csRows.Count}");

        if (psRows.Count == 0 && csRows.Count == 0)
        {
            Output.WriteLine("Both sides empty (Hyper-V unreadable without elevation): parity holds vacuously.");
            return;
        }

        var csJson = JsonSerializer.Serialize(csRows.Select(r => new Dictionary<string, object?>
        {
            ["HostNode"] = r.HostNode,
            ["Name"] = r.Name,
            ["SwitchType"] = r.SwitchType,
            ["NetAdapterInterfaceDescription"] = r.NetAdapterInterfaceDescription,
            ["AllowManagementOS"] = r.AllowManagementOS,
        }));
        var diffs = ParityRow.Diff(psRows, ParityRow.ParseRows(csJson, []),
            r => $"{r.GetValueOrDefault("HostNode")}|{r.GetValueOrDefault("Name")}",
            ["HostNode", "Name", "SwitchType", "AllowManagementOS", "NetAdapterInterfaceDescription"]);
        foreach (var diff in diffs)
        {
            Output.WriteLine(diff);
        }

        Assert.True(diffs.Count == 0, $"Virtual Switch parity failed with {diffs.Count} difference(s).");
    }

    [Fact]
    public async Task VlanParity_Local()
    {
        var snippet = VlanIdentityCanonicalizer + "$node=$env:COMPUTERNAME; $rows=@(); $vlans=Get-VMNetworkAdapterVlan -ComputerName $node -ErrorAction SilentlyContinue; foreach($v in @($vlans)){ $id = Get-PiVlanReferenceIdentity $v; $rows += [PSCustomObject]@{HostNode=$node; VMName=$id.VMName; VMNetworkAdapterName=$id.VMNetworkAdapterName; OperationMode=[string]$v.OperationMode; AccessVlanId=$v.AccessVlanId; NativeVlanId=$v.NativeVlanId; AllowedVlanIdList=$v.AllowedVlanIdList} }; $rows";
        var reference = await PowerShellReference.CollectSnippetAsync(snippet, Modules, ReferenceTimeout);
        if (!reference.IsAvailable)
        {
            ReportNotTestable(Output, reference.Reason);
            return;
        }

        var psRows = ParityRow.ParseRows(reference.Json, ["AccessVlanId", "NativeVlanId"]);
        var querier = new MmiCimQuerier(NullLogger<MmiCimQuerier>.Instance);
        var service = new HyperVService(
            querier,
            new VhdInspector(querier, NullLogger<VhdInspector>.Instance),
            NullLogger<HyperVService>.Instance);
        var results = await service.GetVmAdapterVlansAsync([Environment.MachineName]);
        var csRows = results.Where(r => r.IsSuccess).SelectMany(r => r.Data!).ToList();
        Output.WriteLine($"PS VLAN rows: {psRows.Count}, C# VLAN rows: {csRows.Count}");

        if (psRows.Count == 0 && csRows.Count == 0)
        {
            Output.WriteLine("Both sides empty (Hyper-V unreadable without elevation): parity holds vacuously.");
            return;
        }

        var csJson = JsonSerializer.Serialize(csRows.Select(r => new Dictionary<string, object?>
        {
            ["HostNode"] = r.HostNode,
            ["VMName"] = r.VMName,
            ["VMNetworkAdapterName"] = r.VMNetworkAdapterName,
            ["OperationMode"] = r.OperationMode,
            ["AccessVlanId"] = r.AccessVlanId,
            ["NativeVlanId"] = r.NativeVlanId,
            ["AllowedVlanIdList"] = r.AllowedVlanIdList,
        }));
        var diffs = ParityRow.Diff(psRows, ParityRow.ParseRows(csJson, ["AccessVlanId", "NativeVlanId"]),
            r => $"{r.GetValueOrDefault("HostNode")}|{r.GetValueOrDefault("VMName")}|{r.GetValueOrDefault("VMNetworkAdapterName")}",
            ["HostNode", "VMName", "VMNetworkAdapterName", "OperationMode", "AccessVlanId", "NativeVlanId", "AllowedVlanIdList"]);
        if (diffs.Count > 0)
        {
            // Honest forensics: dump both sides completely, never suppress.
            foreach (var row in psRows)
            {
                Output.WriteLine("PS : " + string.Join(" | ", row.Select(kv => $"{kv.Key}='{kv.Value}'")));
            }

            foreach (var row in ParityRow.ParseRows(csJson, ["AccessVlanId", "NativeVlanId"]))
            {
                Output.WriteLine("C# : " + string.Join(" | ", row.Select(kv => $"{kv.Key}='{kv.Value}'")));
            }
        }

        foreach (var diff in diffs)
        {
            Output.WriteLine(diff);
        }

        Assert.True(diffs.Count == 0, $"VLAN parity failed with {diffs.Count} difference(s).");
    }
}
