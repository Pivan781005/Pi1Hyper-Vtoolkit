using Microsoft.Extensions.Logging.Abstractions;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Services;
using Pi1.HyperVToolkit.Infrastructure.Cim;
using Pi1.HyperVToolkit.Infrastructure.HyperV;
using Pi1.HyperVToolkit.Infrastructure.Mapping;

namespace Pi1.HyperVToolkit.Tests;

public sealed class HyperVPhase4ServiceTests
{
    private const string VmGuid = "AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA";
    private const string Vssd = "Microsoft:VSSD-CURRENT-1";
    private const string NicInstance = "Microsoft:BFA881C4-9E3D-4C61-BE95-E20E0A44E239\\AFB9F2E4-401D-4CD8-8764-2B2C9096FE53";
    private const string PortGuid = "BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB";
    private const string DefaultSwitchGuid = "C08CB7B8-9B3C-408E-8E30-5E16A3AEB444";
    private const string WslSwitchGuid = "790E58B4-7939-4434-9358-89AE7DDBE87E";
    private const string IdeControllerId = @"Microsoft:DOSVM\83F8638B-8DCA-4152-9EDA-2CA8B33039B4\0";
    private const string DosDriveId = @"Microsoft:DOSVM\83F8638B-8DCA-4152-9EDA-2CA8B33039B4\0\0\D";
    private const string DosAvhd =
        @"C:\ProgramData\Microsoft\Windows\Virtual Hard Disks\dos_cd_02C239D7-8622-4B78-B6DB-CC80DC6E62C2.avhd";

    private sealed class FakeQuerier : ICimQuerier
    {
        public Func<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>? Handler { get; set; }

        public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
            string node, string @namespace, string wql, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(node, "BAD", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("host unreachable (test)");
            }

            return Task.FromResult(Handler?.Invoke(wql) ?? []);
        }

        public Task<IReadOnlyDictionary<string, object?>> InvokeSingletonMethodAsync(
            string node, string @namespace, string className, string methodName,
            IReadOnlyDictionary<string, object?> inParameters, TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, object?>>(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
    }

    private sealed class FakeVhd : IVhdInspector
    {
        public Task<VhdMetadata?> InspectAsync(string node, string path, CancellationToken cancellationToken = default)
        {
            if (path.EndsWith("missing.vhdx", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<VhdMetadata?>(null); // inaccessible VHD.
            }

            if (path.EndsWith(".avhd", StringComparison.OrdinalIgnoreCase))
            {
                // Live DOS shape: Differencing VHD, 2 GiB virtual, 81920 B file.
                return Task.FromResult<VhdMetadata?>(new VhdMetadata(
                    path, "Differencing", "VHD", 2147483648UL, 81920UL));
            }

            return Task.FromResult<VhdMetadata?>(new VhdMetadata(
                path, "Dynamic", "VHDX", 10UL * 1024 * 1024 * 1024, 4UL * 1024 * 1024 * 1024));
        }
    }

    private static Dictionary<string, object?> Bag(params (string Key, object? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

    private static FakeQuerier CreateQuerier() => new()
    {
        Handler = wql =>
        {
            if (wql.Contains("Msvm_ComputerSystem", StringComparison.Ordinal))
            {
                return [Bag(("Name", VmGuid), ("ElementName", "VM1"), ("EnabledState", (ushort)2))];
            }

            if (wql.Contains("Msvm_VirtualSystemSettingData", StringComparison.Ordinal) &&
                !wql.Contains("ASSOCIATORS", StringComparison.Ordinal))
            {
                return [Bag(
                    ("__Class", "Msvm_VirtualSystemSettingData"),
                    ("InstanceID", Vssd),
                    ("VirtualSystemIdentifier", VmGuid),
                    ("VirtualSystemType", "Microsoft:Hyper-V:System:Realized"))];
            }

            if (wql.Contains("Msvm_VirtualSystemSettingDataComponent", StringComparison.Ordinal))
            {
                const string floppyId = @"Microsoft:DOSVM\FLOPPY0";
                const string dvdId = @"Microsoft:DOSVM\DVD0";
                return
                [
                    Bag(
                        ("__Class", "Msvm_SyntheticEthernetPortSettingData"),
                        ("InstanceID", NicInstance),
                        ("ElementName", "Network Adapter"),
                        ("Address", "00155D03B501"),
                        ("Connection", new[] { $"Microsoft:Port\\{PortGuid}" })),
                    Bag(
                        ("__Class", "Msvm_EthernetPortAllocationSettingData"),
                        ("InstanceID", NicInstance + "\\C"),
                        ("ElementName", "Dynamic Ethernet Switch Port"),
                        ("Parent", NicInstance),
                        ("HostResource", new[] { $"Msvm_VirtualEthernetSwitch.Name = \"{DefaultSwitchGuid}\"" }),
                        ("EnabledState", (ushort)2)),
                    Bag(
                        ("__Class", "Msvm_ResourceAllocationSettingData"),
                        ("InstanceID", IdeControllerId),
                        ("ResourceType", (ushort)5),
                        ("ResourceSubType", "Microsoft:Hyper-V:Emulated IDE Controller"),
                        ("Address", (ushort)0)),
                    Bag(
                        ("__Class", "Msvm_ResourceAllocationSettingData"),
                        ("InstanceID", DosDriveId),
                        ("ResourceType", (ushort)17),
                        ("ResourceSubType", "Microsoft:Hyper-V:Synthetic Disk Drive"),
                        ("Parent", IdeControllerId),
                        ("AddressOnParent", (ushort)0)),
                    Bag(
                        ("__Class", "Msvm_StorageAllocationSettingData"),
                        ("InstanceID", "Microsoft:Image\\AVHD1"),
                        ("ResourceType", (ushort)31),
                        ("ResourceSubType", "Microsoft:Hyper-V:Virtual Hard Disk"),
                        ("Parent", DosDriveId),
                        ("HostResource", new[] { DosAvhd })),
                    Bag(
                        ("__Class", "Msvm_ResourceAllocationSettingData"),
                        ("InstanceID", floppyId),
                        ("ResourceType", (ushort)14),
                        ("ResourceSubType", "Microsoft:Hyper-V:Synthetic Diskette Drive"),
                        ("Parent", IdeControllerId),
                        ("AddressOnParent", (ushort)0)),
                    Bag(
                        ("__Class", "Msvm_StorageAllocationSettingData"),
                        ("InstanceID", "Microsoft:Image\\VFD1"),
                        ("ResourceType", (ushort)31),
                        ("ResourceSubType", "Microsoft:Hyper-V:Virtual Floppy Disk"),
                        ("Parent", floppyId),
                        ("HostResource", new[] { @"C:\Users\Pivan\Downloads\Microsoft Mouse 8.20.vfd" })),
                    Bag(
                        ("__Class", "Msvm_ResourceAllocationSettingData"),
                        ("InstanceID", dvdId),
                        ("ResourceType", (ushort)16),
                        ("ResourceSubType", "Microsoft:Hyper-V:Synthetic DVD Drive"),
                        ("Parent", IdeControllerId),
                        ("AddressOnParent", (ushort)1)),
                    Bag(
                        ("__Class", "Msvm_StorageAllocationSettingData"),
                        ("InstanceID", "Microsoft:Image\\ISO1"),
                        ("ResourceType", (ushort)31),
                        ("ResourceSubType", "Microsoft:Hyper-V:Virtual CD/DVD Disk"),
                        ("Parent", dvdId),
                        ("HostResource", new[] { @"C:\Users\Pivan\Downloads\MS-DOS 6.22.iso" })),
                ];
            }

            if (wql.Contains("Msvm_VirtualEthernetSwitch", StringComparison.Ordinal) &&
                !wql.Contains("ASSOCIATORS", StringComparison.Ordinal))
            {
                return
                [
                    Bag(("Name", DefaultSwitchGuid), ("ElementName", "Default Switch")),
                    Bag(("Name", WslSwitchGuid), ("ElementName", "WSL (Hyper-V firewall)")),
                ];
            }

            if (wql.Contains("Msvm_EthernetPortAllocationSettingData", StringComparison.Ordinal) &&
                !wql.Contains("ASSOCIATORS", StringComparison.Ordinal))
            {
                // Host-wide host-vNIC allocations: FIRST GUID is the switch GUID.
                return
                [
                    Bag(
                        ("ElementName", $"Host Vnic {DefaultSwitchGuid}"),
                        ("InstanceID", $"Microsoft:{DefaultSwitchGuid}\\{DefaultSwitchGuid[..8]}44444445"),
                        ("HostResource", new[] { "Msvm_ComputerSystem.Name=\"LATITUDE-5590\"" }),
                        ("EnabledState", (ushort)2)),
                    Bag(
                        ("ElementName", $"Host Vnic {WslSwitchGuid}"),
                        ("InstanceID", $"Microsoft:{WslSwitchGuid}\\{WslSwitchGuid[..8]}4444447F"),
                        ("HostResource", new[] { "Msvm_ComputerSystem.Name=\"LATITUDE-5590\"" }),
                        ("EnabledState", (ushort)2)),
                ];
            }

            if (wql.Contains("Msvm_InternalEthernetPort", StringComparison.Ordinal) &&
                !wql.Contains("ASSOCIATORS", StringComparison.Ordinal))
            {
                return
                [
                    Bag(
                        ("InstanceID", "Microsoft:Host\\DSW"),
                        ("DeviceID", "Microsoft:Host\\DSW"),
                        ("Name", "C08CB7B8-9B3C-408E-8E30-5E16A3AEB445"),
                        ("ElementName", "Default Switch"),
                        ("PermanentAddress", "00155D03B500"),
                        ("NetworkAddresses", new[] { "00155D03B500" })),
                    Bag(
                        ("InstanceID", "Microsoft:Host\\WSL"),
                        ("DeviceID", "Microsoft:Host\\WSL"),
                        ("Name", "790E58B4-7939-4434-9358-89AE7DDBE87F"),
                        ("ElementName", "WSL (Hyper-V firewall)"),
                        ("PermanentAddress", "00155D56730B"),
                        ("NetworkAddresses", new[] { "00155D56730B" })),
                ];
            }

            if (wql.Contains("Msvm_ExternalEthernetPort", StringComparison.Ordinal))
            {
                return [];
            }

            if (wql.Contains("Msvm_EthernetSwitchPortVlanSettingData", StringComparison.Ordinal))
            {
                return [Bag(
                    ("InstanceID", $"Microsoft:VLAN\\{PortGuid}\\Setting"),
                    ("OperationMode", (uint)1),
                    ("AccessVlanId", (ushort)20),
                    ("NativeVlanId", (ushort)0),
                    ("TrunkVlanIdArray", Array.Empty<ushort>()))];
            }

            return [];
        },
    };

    private static HyperVService CreateService(FakeQuerier querier) =>
        new(querier, new FakeVhd(), NullLogger<HyperVService>.Instance);

    [Fact]
    public async Task Switches_InternalViaHostVnicAllocation()
    {
        // Live shape: direct InternalEthernetPort→switch ASSOCIATORS return
        // nothing, but active Host Vnic allocations prove both switches are
        // Internal with management-OS attachment.
        var service = CreateService(CreateQuerier());
        var results = await service.GetVirtualSwitchesAsync(["N1"]);
        var rows = Assert.Single(results, r => r.IsSuccess).Data!
            .OrderBy(r => r.Name).ToList();
        Assert.Equal(2, rows.Count);

        var defaultSwitch = Assert.Single(rows, r => r.Name == "Default Switch");
        Assert.Equal("Internal", defaultSwitch.SwitchType);
        Assert.True(defaultSwitch.AllowManagementOS);

        var wsl = Assert.Single(rows, r => r.Name == "WSL (Hyper-V firewall)");
        Assert.Equal("Internal", wsl.SwitchType);
        Assert.True(wsl.AllowManagementOS);
    }

    [Fact]
    public void Switches_ExternalEvidence_Combinations()
    {
        // Pure evidence-matrix supplement (Task 9): MapSwitchRow booleans are
        // collected facts; the matrix itself is pinned here.
        Assert.Equal(("External", true), (HyperVMapper.MapSwitchRow("N", "S", true, true, "").SwitchType, HyperVMapper.MapSwitchRow("N", "S", true, true, "").AllowManagementOS));
        var extNoMgmt = HyperVMapper.MapSwitchRow("N", "S", true, false, "");
        Assert.Equal("External", extNoMgmt.SwitchType);
        Assert.False(extNoMgmt.AllowManagementOS);
        var priv = HyperVMapper.MapSwitchRow("N", "S", false, false, "");
        Assert.Equal("Private", priv.SwitchType);
        Assert.False(priv.AllowManagementOS);
    }

    [Fact]
    public async Task NicSwitch_AllocationResolvesSwitch_ServiceLevel()
    {
        // Current NIC has Connection[] pointing at a VLAN port, but the
        // CURRENT allocation's HostResource is authoritative for the switch.
        var service = CreateService(CreateQuerier());
        var results = await service.GetVmNetworkAdaptersAsync(["N1"]);
        var rows = Assert.Single(results, r => r.IsSuccess).Data!.ToList();
        var vmRow = Assert.Single(rows, r => r.VMName == "VM1");
        Assert.Equal("00155D03B501", vmRow.MacAddress);
        Assert.Equal("Default Switch", vmRow.SwitchName);
    }

    [Fact]
    public async Task Vlan_CurrentNicOnly_AccessMode()
    {
        var service = CreateService(CreateQuerier());
        var results = await service.GetVmAdapterVlansAsync(["N1"]);
        var row = Assert.Single(Assert.Single(results, r => r.IsSuccess).Data!);
        Assert.Equal("VM1", row.VMName);
        Assert.Equal("Network Adapter", row.VMNetworkAdapterName);
        Assert.Equal("Access", row.OperationMode);
        Assert.Equal(20, row.AccessVlanId);
    }

    [Fact]
    public async Task Storage_RealDosGraph_SingleAvhdRow()
    {
        // Live DOS shape: exactly ONE storage row (IDE 0:0, Differencing VHD,
        // 2.0/0.0 GB); the .vfd and .iso images must never appear.
        var service = CreateService(CreateQuerier());
        var results = await service.GetVmStorageAsync(["N1"], []);
        var row = Assert.Single(Assert.Single(results, r => r.IsSuccess).Data!);
        Assert.Equal("VM1", row.VM);
        Assert.Equal("Running", row.State);
        Assert.Equal("IDE 0:0", row.Controller);
        Assert.Equal("VHD", row.VHDFormat);
        Assert.Equal("Differencing", row.VHDType);
        Assert.Equal(2.0, row.VHDSizeGB);
        Assert.Equal(0.0, row.VHDFileGB); // 81920 B => present zero, not null.
        Assert.NotNull(row.VHDFileGB);
        Assert.Equal(string.Empty, row.CSV); // DOS disk lives outside CSV.
        Assert.Equal(DosAvhd, row.Path);
    }

    [Fact]
    public async Task Storage_InaccessibleVhd_UnknownFallback_RowPreserved()
    {
        // One bad disk: Unknown/empty user-visible semantics, row preserved,
        // no batch failure.
        const string ctrl = @"Microsoft:VM2\C0";
        const string drive = @"Microsoft:VM2\C0\D0";
        var querier = new FakeQuerier
        {
            Handler = wql =>
            {
                if (wql.Contains("Msvm_ComputerSystem", StringComparison.Ordinal))
                {
                    return [Bag(("Name", VmGuid), ("ElementName", "VM2"), ("EnabledState", (ushort)2))];
                }

                if (wql.Contains("Msvm_VirtualSystemSettingData", StringComparison.Ordinal) &&
                    !wql.Contains("ASSOCIATORS", StringComparison.Ordinal))
                {
                    return [Bag(
                        ("__Class", "Msvm_VirtualSystemSettingData"),
                        ("InstanceID", Vssd),
                        ("VirtualSystemIdentifier", VmGuid),
                        ("VirtualSystemType", "Microsoft:Hyper-V:System:Realized"))];
                }

                if (wql.Contains("Msvm_VirtualSystemSettingDataComponent", StringComparison.Ordinal))
                {
                    return
                    [
                        Bag(
                            ("__Class", "Msvm_ResourceAllocationSettingData"),
                            ("InstanceID", ctrl),
                            ("ResourceType", (ushort)6),
                            ("ResourceSubType", "Microsoft:Hyper-V:Synthetic SCSI Controller"),
                            ("Address", (ushort)0)),
                        Bag(
                            ("__Class", "Msvm_ResourceAllocationSettingData"),
                            ("InstanceID", drive),
                            ("ResourceType", (ushort)17),
                            ("ResourceSubType", "Microsoft:Hyper-V:Synthetic Disk Drive"),
                            ("Parent", ctrl),
                            ("AddressOnParent", (ushort)0)),
                        Bag(
                            ("__Class", "Msvm_StorageAllocationSettingData"),
                            ("InstanceID", "Microsoft:Image\\MISS"),
                            ("ResourceType", (ushort)31),
                            ("ResourceSubType", "Microsoft:Hyper-V:Virtual Hard Disk"),
                            ("Parent", drive),
                            ("HostResource", new[] { @"D:\VMs\missing.vhdx" })),
                    ];
                }

                return [];
            },
        };
        var service = CreateService(querier);
        var results = await service.GetVmStorageAsync(["N1"], []);
        var row = Assert.Single(Assert.Single(results, r => r.IsSuccess).Data!);
        Assert.Equal("SCSI 0:0", row.Controller);
        Assert.Equal("Unknown", row.VHDType);
        Assert.Equal("Unknown", row.VHDFormat);
        Assert.Null(row.VHDSizeGB);
        Assert.Null(row.VHDFileGB);
    }

    [Fact]
    public async Task PartialFailure_OtherNodeContinues()
    {
        var service = CreateService(CreateQuerier());
        var results = await service.GetVirtualSwitchesAsync(["N1", "BAD"]);
        Assert.Equal(2, results.Count);
        Assert.True(results.First(r => r.Node == "N1").IsSuccess);
        var bad = results.First(r => r.Node == "BAD");
        Assert.False(bad.IsSuccess);
        Assert.NotNull(bad.Error);
    }

    [Fact]
    public async Task CancelledToken_Propagates()
    {
        var service = CreateService(CreateQuerier());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetVirtualSwitchesAsync(["N1"], cts.Token));
    }
}
