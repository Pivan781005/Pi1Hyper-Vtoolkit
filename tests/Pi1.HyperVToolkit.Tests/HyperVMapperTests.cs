using Pi1.HyperVToolkit.Infrastructure.HyperV;
using Pi1.HyperVToolkit.Infrastructure.Mapping;
using static Pi1.HyperVToolkit.Infrastructure.Mapping.HyperVMapper;

namespace Pi1.HyperVToolkit.Tests;

/// <summary>
/// NIC mapping regression tests for the corrected design:
/// - VM NICs come ONLY from the CURRENT VSSD components (synthetic or emulated);
/// - DOS 6.22 is SYNTHETIC and disconnected (SwitchName "");
/// - Management OS adapters come from Msvm_InternalEthernetPort;
/// - raw allocation objects never become rows;
/// - StaticMacAddress (boolean) never becomes a MAC string.
/// </summary>
public sealed class HyperVMapperTests
{
    private const string VmGuid = "11111111-1111-1111-1111-111111111111";
    private const string Nic1 = "22222222-2222-2222-2222-222222222222";
    private const string Nic2 = "33333333-3333-3333-3333-333333333333";
    private const string DosGuid = "AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA";
    private const string DosAdapter = "AFB9F2E4-401D-4CD8-8764-2B2C9096FE53";
    private const string DosVssdCurrent = "BFA881C4-9E3D-4C61-BE95-E20E0A44E239";

    private static Dictionary<string, object?> Bag(params (string Key, object? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, object?> RunningSystem(string guid = VmGuid, string name = "APP1") => Bag(
        ("Name", guid),
        ("ElementName", name),
        ("EnabledState", (ushort)2),
        ("OnTimeInMilliseconds", (ulong)7_200_000));

    private static Dictionary<string, object?> Summary(double usageMb, double bufferMb, ulong upSeconds = 3600) => Bag(
        ("Name", VmGuid),
        ("NumberOfProcessors", (ushort)4),
        ("MemoryUsage", usageMb),
        ("AvailableMemoryBuffer", bufferMb),
        ("UpTime", upSeconds));

    private static Dictionary<string, object?> ProcSetting() => Bag(
        ("InstanceID", $"Microsoft:{VmGuid}\\Processor"),
        ("ResourceType", (ushort)3),
        ("VirtualQuantity", (ulong)4));

    private static Dictionary<string, object?> MemSetting() => Bag(
        ("InstanceID", $"Microsoft:{VmGuid}\\Memory"),
        ("ResourceType", (ushort)4),
        ("VirtualQuantity", (ulong)4096),
        ("Reservation", (ulong)1024),
        ("Limit", (ulong)8192),
        ("DynamicMemoryEnabled", true));

    private static Dictionary<string, object?> SyntheticComponent(
        string adapterGuid, string mac, string connectionSwitchPortGuid = "") => Bag(
        ("__Class", "Msvm_SyntheticEthernetPortSettingData"),
        ("InstanceID", $"Microsoft:ParentVssd\\{adapterGuid}"),
        ("ElementName", "Network Adapter"),
        ("Address", mac),
        ("StaticMacAddress", false),
        ("Connection", string.IsNullOrEmpty(connectionSwitchPortGuid)
            ? []
            : new[] { $"\\\\HOST\\root\\virtualization\\v2:Msvm_EthernetSwitchPort.DeviceID=\"Microsoft:{connectionSwitchPortGuid}\"" }));

    private static Dictionary<string, object?> EmulatedComponent(string adapterGuid, string mac) => Bag(
        ("__Class", "Msvm_EmulatedEthernetPortSettingData"),
        ("InstanceID", $"Microsoft:ParentVssd\\{adapterGuid}"),
        ("ElementName", "Legacy Network Adapter"),
        ("Address", mac),
        ("StaticMacAddress", false),
        ("Connection", new string[] { }));

    private static Dictionary<string, object?> GuestConfig(string nicGuid, string vmGuid, params string[] ips) => Bag(
        ("InstanceID", $"Microsoft:{vmGuid}\\{nicGuid}"),
        ("IPAddresses", ips));

    private static Dictionary<string, object?> InternalPort(string name, string mac) => Bag(
        ("InstanceID", $"Microsoft:HostVssd\\Internal-{name}"),
        ("DeviceID", $"Microsoft:HostVssd\\Internal-{name}"),
        ("ElementName", name),
        ("PermanentAddress", mac),
        ("NetworkAddresses", new[] { mac }));

    private static Dictionary<string, List<MergedNic>> NoVmNics() =>
        new(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, IReadOnlyDictionary<string, object?>> NoSummaries() =>
        new(StringComparer.OrdinalIgnoreCase);

    [Theory]
    [InlineData((ushort)2, "Running")]
    [InlineData((ushort)3, "Off")]
    [InlineData((ushort)4, "Stopping")]
    [InlineData((ushort)10, "Starting")]
    [InlineData(32768, "Paused")]
    [InlineData(32769, "Saved")]
    [InlineData(9999, "Unknown (9999)")]
    [InlineData(null, "Unknown")]
    public void MapVmState_MapsKnownValues(object? raw, string expected)
    {
        Assert.Equal(expected, HyperVMapper.MapVmState(raw));
    }

    [Fact]
    public void SyntheticConnectedNic_ResolvesMacSwitchAndIps()
    {
        var nics = HyperVMapper.SelectCurrentVmNics(
            VmGuid, "APP1",
            [SyntheticComponent(Nic1, "00155D010203", "99999999-9999-9999-9999-999999999999")],
            new Dictionary<string, string>(),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["99999999-9999-9999-9999-999999999999"] = "vSwitch-LAN",
            });

        var nic = Assert.Single(nics);
        Assert.Equal(VmGuid, nic.VmGuid);
        Assert.Equal("APP1", nic.VmName);
        Assert.Equal("00155D010203", nic.Mac);
        Assert.Equal("vSwitch-LAN", nic.Switch);
        Assert.Equal("Network Adapter", nic.AdapterName);
    }

    [Fact]
    public void SyntheticDisconnectedNic_SwitchStaysEmpty()
    {
        // DOS 6.22 real-host fact: synthetic (IsLegacy=False), Connection={},
        // therefore SwitchName MUST be "" even though a Default Switch exists.
        var nics = HyperVMapper.SelectCurrentVmNics(
            DosGuid, "DOS 6.22",
            [SyntheticComponent(DosAdapter, "00155D03B501")],
            new Dictionary<string, string>(),
            new Dictionary<string, string>());

        var nic = Assert.Single(nics);
        Assert.Equal("00155D03B501", nic.Mac);
        Assert.Equal(string.Empty, nic.Switch);
    }

    [Fact]
    public void DosLikeNic_FullRowWithoutGuestIp()
    {
        var vmNics = new Dictionary<string, List<MergedNic>>(StringComparer.OrdinalIgnoreCase)
        {
            [DosGuid] = HyperVMapper.SelectCurrentVmNics(
                DosGuid, "DOS 6.22",
                [SyntheticComponent(DosAdapter, "00155D03B501")],
                new Dictionary<string, string>(),
                new Dictionary<string, string>()).ToList(),
        };
        var rows = HyperVMapper.MapVmRows(
            "LATITUDE-5590", [RunningSystem(DosGuid, "DOS 6.22")],
            NoSummaries(), [], [],
            vmNics, []);

        var row = Assert.Single(rows);
        Assert.Equal("DOS 6.22", row.VM);
        Assert.Equal("00155D03B501", row.MAC);
        Assert.Equal(string.Empty, row.Switch);
        Assert.Equal(string.Empty, row.IPv4);
    }

    [Fact]
    public void DuplicateAdapterIdAcrossVssds_CurrentSelectionWins()
    {
        // Authoritative evidence: TWO synthetic settings share AdapterId
        // AFB9F2E4... under different parent VSSDs. Only the CURRENT VSSD's
        // components are passed in, so exactly one NIC must result.
        var current = SyntheticComponent(DosAdapter, "00155D03B501");
        var nics = HyperVMapper.SelectCurrentVmNics(
            DosGuid, "DOS 6.22", [current],
            new Dictionary<string, string>(), new Dictionary<string, string>());

        var nic = Assert.Single(nics);
        Assert.Equal("00155D03B501", nic.Mac);
    }

    [Fact]
    public void DuplicateAdapterIdWithinOneVssd_Deduplicated()
    {
        var nics = HyperVMapper.SelectCurrentVmNics(
            VmGuid, "APP1",
            [SyntheticComponent(Nic1, "00155D010203"), SyntheticComponent(Nic1, "00155D010203")],
            new Dictionary<string, string>(), new Dictionary<string, string>());
        Assert.Single(nics);
    }

    [Fact]
    public void NonEthernetComponents_Ignored()
    {
        var memory = Bag(
            ("__Class", "Msvm_MemorySettingData"),
            ("InstanceID", $"Microsoft:ParentVssd\\Memory"),
            ("Address", "00155D999999"));
        var nics = HyperVMapper.SelectCurrentVmNics(
            VmGuid, "APP1", [memory],
            new Dictionary<string, string>(), new Dictionary<string, string>());
        Assert.Empty(nics);
    }

    [Fact]
    public void StaticMacAddressFalse_NeverBecomesMac()
    {
        var setting = Bag(
            ("__Class", "Msvm_SyntheticEthernetPortSettingData"),
            ("InstanceID", $"Microsoft:ParentVssd\\{Nic1}"),
            ("ElementName", "Network Adapter"),
            ("Address", ""),
            ("StaticMacAddress", false),
            ("Connection", new string[] { }));
        var nics = HyperVMapper.SelectCurrentVmNics(
            VmGuid, "APP1", [setting],
            new Dictionary<string, string>(), new Dictionary<string, string>());

        var nic = Assert.Single(nics);
        Assert.Equal(string.Empty, nic.Mac);
    }

    [Fact]
    public void ActivePort_BackfillsMissingMac()
    {
        var setting = Bag(
            ("__Class", "Msvm_SyntheticEthernetPortSettingData"),
            ("InstanceID", $"Microsoft:ParentVssd\\{Nic1}"),
            ("ElementName", "Network Adapter"),
            ("Address", ""),
            ("StaticMacAddress", false),
            ("Connection", new string[] { }));
        var activeMacs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Nic1] = "00155D09AABB",
        };
        var nics = HyperVMapper.SelectCurrentVmNics(
            VmGuid, "APP1", [setting], activeMacs,
            new Dictionary<string, string>());
        Assert.Equal("00155D09AABB", Assert.Single(nics).Mac);
    }

    [Fact]
    public void EmulatedNic_SupportedSeparately()
    {
        var nics = HyperVMapper.SelectCurrentVmNics(
            VmGuid, "APP1",
            [EmulatedComponent(Nic1, "00155D0E0E01")],
            new Dictionary<string, string>(), new Dictionary<string, string>());

        var nic = Assert.Single(nics);
        Assert.Equal("00155D0E0E01", nic.Mac);
        Assert.Equal("Legacy Network Adapter", nic.AdapterName);
    }

    [Fact]
    public void MgmtNics_WslAndDefaultSwitchExactRows()
    {
        var nics = HyperVMapper.SelectMgmtNics([
            InternalPort("WSL (Hyper-V firewall)", "00155D56730B"),
            InternalPort("Default Switch", "00155D03B500"),
        ]);

        Assert.Equal(2, nics.Count);
        Assert.All(nics, n =>
        {
            Assert.Null(n.VmGuid);
            Assert.Equal(string.Empty, n.VmName);
        });
        Assert.Contains(nics, n => n.Mac == "00155D56730B" && n.Switch == "WSL (Hyper-V firewall)");
        Assert.Contains(nics, n => n.Mac == "00155D03B500" && n.Switch == "Default Switch");
    }

    [Fact]
    public void MgmtNic_WithoutAddress_Skipped()
    {
        var nics = HyperVMapper.SelectMgmtNics([
            Bag(
                ("InstanceID", "Microsoft:Host\\NoAddr"),
                ("DeviceID", "Microsoft:Host\\NoAddr"),
                ("ElementName", "Ghost"),
                ("PermanentAddress", ""),
                ("NetworkAddresses", new string[] { })),
        ]);
        Assert.Empty(nics);
    }

    [Fact]
    public void MapNicRows_AllShape_VmPlusMgmt()
    {
        var vmNics = new Dictionary<string, List<MergedNic>>(StringComparer.OrdinalIgnoreCase)
        {
            [DosGuid] = HyperVMapper.SelectCurrentVmNics(
                DosGuid, "DOS 6.22",
                [SyntheticComponent(DosAdapter, "00155D03B501")],
                new Dictionary<string, string>(), new Dictionary<string, string>()).ToList(),
        };
        var mgmt = HyperVMapper.SelectMgmtNics([
            InternalPort("WSL (Hyper-V firewall)", "00155D56730B"),
            InternalPort("Default Switch", "00155D03B500"),
        ]);

        var rows = HyperVMapper.MapNicRows("LATITUDE-5590", vmNics.Values.SelectMany(v => v), mgmt, []);

        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, r => r.VMName == string.Empty && r.MacAddress == "00155D56730B" && r.SwitchName == "WSL (Hyper-V firewall)");
        Assert.Contains(rows, r => r.VMName == string.Empty && r.MacAddress == "00155D03B500" && r.SwitchName == "Default Switch");
        Assert.Contains(rows, r => r.VMName == "DOS 6.22" && r.MacAddress == "00155D03B501" && r.SwitchName == string.Empty);
    }

    [Fact]
    public void MapVmRows_MultiNic_ProducesOneRowPerNic()
    {
        var vmNics = new Dictionary<string, List<MergedNic>>(StringComparer.OrdinalIgnoreCase)
        {
            [VmGuid] = HyperVMapper.SelectCurrentVmNics(
                VmGuid, "APP1",
                [SyntheticComponent(Nic1, "00155D010203"), SyntheticComponent(Nic2, "00155D040506")],
                new Dictionary<string, string>(), new Dictionary<string, string>()).ToList(),
        };
        var rows = HyperVMapper.MapVmRows(
            "NODE1", [RunningSystem()],
            new Dictionary<string, IReadOnlyDictionary<string, object?>> { [VmGuid] = Summary(8192, 1024) },
            [ProcSetting()], [MemSetting()], vmNics,
            [GuestConfig(Nic1, VmGuid, "192.168.1.10", "fe80::1"), GuestConfig(Nic2, VmGuid, "10.0.0.5")]);

        Assert.Equal(2, rows.Count);
        Assert.Equal("192.168.1.10", rows[0].IPv4);
        Assert.Equal("00155D010203", rows[0].MAC);
        Assert.Equal("10.0.0.5", rows[1].IPv4);
        Assert.Equal(8.0, rows[0].AssignedGB);
        Assert.Equal(1.0, rows[0].WasteGB);
    }

    [Fact]
    public void MapVmRows_NoNic_EmitsSingleRowWithEmptyNetwork()
    {
        var rows = HyperVMapper.MapVmRows(
            "NODE1", [RunningSystem()],
            new Dictionary<string, IReadOnlyDictionary<string, object?>> { [VmGuid] = Summary(2048, 0) },
            [ProcSetting()], [MemSetting()], NoVmNics(), []);

        var row = Assert.Single(rows);
        Assert.Equal(string.Empty, row.IPv4);
        Assert.Equal(string.Empty, row.MAC);
        Assert.Equal(string.Empty, row.Switch);
    }

    [Fact]
    public void MapVmRows_StoppedVm_ReportsZeroMemoryAndDashUptime()
    {
        var system = Bag(
            ("Name", VmGuid),
            ("ElementName", "APP1"),
            ("EnabledState", (ushort)3));
        var rows = HyperVMapper.MapVmRows("NODE1", [system], NoSummaries(), [ProcSetting()], [MemSetting()], NoVmNics(), []);

        var row = Assert.Single(rows);
        Assert.Equal("Off", row.State);
        Assert.Equal(0, row.AssignedGB);
        Assert.Equal("-", row.UptimeText);
    }

    [Fact]
    public void MapCheckpoints_KeepsOnlySnapshotsAndResolvesVm()
    {
        var snapshot = Bag(
            ("ElementName", "Before patch"),
            ("VirtualSystemType", "Microsoft:Hyper-V:Snapshot:Full"),
            ("VirtualSystemSubType", ""),
            ("VirtualSystemIdentifier", "44444444-4444-4444-4444-444444444444"),
            ("Parent", $"Microsoft:Hyper-V:System:Realized\\{VmGuid}"),
            ("InstanceID", "Microsoft:Snap:1"),
            ("CreationTime", "20260101120000.000000+060"));
        var realized = Bag(
            ("ElementName", "APP1"),
            ("VirtualSystemType", "Microsoft:Hyper-V:System:Realized"));

        var rows = HyperVMapper.MapCheckpoints(
            "NODE1", [snapshot, realized],
            new Dictionary<string, string> { [VmGuid] = "APP1" });

        var row = Assert.Single(rows);
        Assert.Equal("APP1", row.VM);
        Assert.Equal("Standard", row.Type);
        Assert.NotNull(row.Created);
    }

    [Fact]
    public void MapHostSettings_MapsAllFields()
    {
        var row = HyperVMapper.MapHostSettings(
            "NODE1",
            Bag(
                ("NumaSpanningEnabled", true),
                ("EnhancedSessionModeEnabled", false),
                ("DefaultExternalDataRoot", @"C:\VMs"),
                ("DefaultVirtualHardDiskPath", @"C:\VHDs")),
            Bag(
                ("EnableVirtualSystemMigration", true),
                ("MaximumActiveVirtualSystemMigration", (uint)2)),
            null);

        Assert.Equal("NODE1", row.Node);
        Assert.True(row.NumaSpanningEnabled);
        Assert.Equal(2, row.MaxMigrations);
        Assert.Equal(@"C:\VHDs", row.VirtualHardDiskPath);
    }

    [Fact]
    public void AssociationQueries_BuildValidWql()
    {
        var components = AssociationQueries.ComponentsOfSetting(@"Microsoft:AAA-BBB\CCC");
        Assert.Contains("ASSOCIATORS OF", components);
        Assert.Contains("Msvm_VirtualSystemSettingDataComponent", components);
        Assert.Contains(@"Microsoft:AAA-BBB\CCC", components);

        var ports = AssociationQueries.PortsOfSwitch("11111111-1111-1111-1111-111111111111");
        Assert.Contains("Msvm_VirtualEthernetSwitch", ports);
        Assert.Contains("Msvm_EthernetSwitchPort", ports);
    }
}
