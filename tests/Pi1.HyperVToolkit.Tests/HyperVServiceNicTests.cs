using Microsoft.Extensions.Logging.Abstractions;
using Pi1.HyperVToolkit.Infrastructure.Cim;
using Pi1.HyperVToolkit.Infrastructure.HyperV;

namespace Pi1.HyperVToolkit.Tests;

/// <summary>
/// Service-level proof for the corrected NIC design on the real DOS 6.22 shape:
/// current-VSSD associators select the live synthetic NIC, Management OS rows
/// come from internal ports, and raw allocation objects are never queried.
/// </summary>
public sealed class HyperVServiceNicTests
{
    private const string DosGuid = "AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA";
    private const string DosAdapter = "AFB9F2E4-401D-4CD8-8764-2B2C9096FE53";
    private const string CurrentVssd = "Microsoft:BFA881C4-9E3D-4C61-BE95-E20E0A44E239";
    private const string HistoricalVssd = "Microsoft:BE3EFFC7-BA1E-4996-A56D-C77B68D054FE";

    private sealed class FakeQuerier : ICimQuerier
    {
        public List<string> ReceivedWql { get; } = [];

        private static Dictionary<string, object?> Bag(params (string Key, object? Value)[] pairs) =>
            pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
            string node, string @namespace, string wql, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            ReceivedWql.Add(wql);

            if (wql.Contains("Msvm_ComputerSystem", StringComparison.Ordinal))
            {
                return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>([
                    Bag(("Name", DosGuid), ("ElementName", "DOS 6.22"), ("EnabledState", (ushort)2)),
                ]);
            }

            if (wql.Contains("Msvm_VirtualSystemSettingData", StringComparison.Ordinal) &&
                !wql.Contains("ASSOCIATORS", StringComparison.Ordinal))
            {
                return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>([
                    Bag(
                        ("__Class", "Msvm_VirtualSystemSettingData"),
                        ("InstanceID", CurrentVssd),
                        ("VirtualSystemIdentifier", DosGuid),
                        ("VirtualSystemType", "Microsoft:Hyper-V:System:Realized")),
                    Bag(
                        ("__Class", "Msvm_VirtualSystemSettingData"),
                        ("InstanceID", HistoricalVssd),
                        ("VirtualSystemIdentifier", "99999999-9999-9999-9999-999999999999"),
                        ("VirtualSystemType", "Microsoft:Hyper-V:Snapshot:Full")),
                ]);
            }

            if (wql.Contains("Msvm_VirtualSystemSettingDataComponent", StringComparison.Ordinal))
            {
                return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>([
                    Bag(
                        ("__Class", "Msvm_SyntheticEthernetPortSettingData"),
                        ("InstanceID", $"Microsoft:Parent\\{DosAdapter}"),
                        ("ElementName", "Network Adapter"),
                        ("Address", "00155D03B501"),
                        ("StaticMacAddress", false),
                        ("Connection", new string[] { })),
                ]);
            }

            if (wql.Contains("Msvm_VirtualEthernetSwitch", StringComparison.Ordinal) &&
                !wql.Contains("ASSOCIATORS", StringComparison.Ordinal))
            {
                return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>([
                    Bag(("Name", "SWITCH-GUID-1"), ("ElementName", "Default Switch")),
                ]);
            }

            if (wql.Contains("Msvm_InternalEthernetPort", StringComparison.Ordinal))
            {
                return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>([
                    Bag(
                        ("InstanceID", "Microsoft:Host\\WSL"),
                        ("DeviceID", "Microsoft:Host\\WSL"),
                        ("ElementName", "WSL (Hyper-V firewall)"),
                        ("PermanentAddress", "00155D56730B"),
                        ("NetworkAddresses", new[] { "00155D56730B" })),
                    Bag(
                        ("InstanceID", "Microsoft:Host\\DSW"),
                        ("DeviceID", "Microsoft:Host\\DSW"),
                        ("ElementName", "Default Switch"),
                        ("PermanentAddress", "00155D03B500"),
                        ("NetworkAddresses", new[] { "00155D03B500" })),
                ]);
            }

            return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>([]);
        }

        public Task<IReadOnlyDictionary<string, object?>> InvokeSingletonMethodAsync(
            string node, string @namespace, string className, string methodName,
            IReadOnlyDictionary<string, object?> inParameters, TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, object?>>(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DosScenario_EndToEnd()
    {
        var querier = new FakeQuerier();
        var service = new HyperVService(
            querier, new NullVhdInspector(), NullLogger<HyperVService>.Instance);

        var vmResults = await service.GetVirtualMachinesAsync(["N1"]);
        var vmRows = Assert.Single(vmResults, r => r.IsSuccess).Data!;
        var vmRow = Assert.Single(vmRows);
        Assert.Equal("DOS 6.22", vmRow.VM);
        Assert.Equal("00155D03B501", vmRow.MAC);
        Assert.Equal(string.Empty, vmRow.Switch);
        Assert.Equal(string.Empty, vmRow.IPv4);

        var nicResults = await service.GetVmNetworkAdaptersAsync(["N1"]);
        var nicRows = Assert.Single(nicResults, r => r.IsSuccess).Data!;
        Assert.Equal(3, nicRows.Count);
        Assert.Contains(nicRows, r => r.VMName == "DOS 6.22" && r.MacAddress == "00155D03B501" && r.SwitchName == string.Empty);
        Assert.Contains(nicRows, r => r.VMName == string.Empty && r.MacAddress == "00155D56730B" && r.SwitchName == "WSL (Hyper-V firewall)");
        Assert.Contains(nicRows, r => r.VMName == string.Empty && r.MacAddress == "00155D03B500" && r.SwitchName == "Default Switch");

        // No allocation-setting object may ever be sourced as an adapter.
        Assert.DoesNotContain(querier.ReceivedWql,
            wql => wql.Contains("AllocationSettingData", StringComparison.Ordinal));

        // Component association must target the CURRENT VSSD only
        // (one component query per collector call: VMs + network inventory).
        var componentQueries = querier.ReceivedWql
            .Where(wql => wql.Contains("Msvm_VirtualSystemSettingDataComponent", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(2, componentQueries.Count);
        Assert.All(componentQueries, q => Assert.Contains(CurrentVssd, q));
        Assert.DoesNotContain(querier.ReceivedWql,
            wql => wql.Contains(HistoricalVssd, StringComparison.Ordinal));
    }

    private sealed class NullVhdInspector : Pi1.HyperVToolkit.Core.Services.IVhdInspector
    {
        public Task<Pi1.HyperVToolkit.Core.Services.VhdMetadata?> InspectAsync(
            string node, string path, CancellationToken cancellationToken = default) =>
            Task.FromResult<Pi1.HyperVToolkit.Core.Services.VhdMetadata?>(null);
    }
}
