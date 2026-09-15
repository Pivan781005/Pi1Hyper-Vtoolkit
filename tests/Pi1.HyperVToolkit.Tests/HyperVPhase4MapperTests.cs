using Pi1.HyperVToolkit.Core.Storage;
using Pi1.HyperVToolkit.Infrastructure.Mapping;

namespace Pi1.HyperVToolkit.Tests;

public sealed class HyperVPhase4MapperTests
{
    private static Dictionary<string, object?> Bag(params (string Key, object? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

    // ---- VLAN operation modes (MS docs: 1 Access, 2 Trunk, 3 Private) ----

    [Theory]
    [InlineData(null, "Untagged")]
    [InlineData(0, "Untagged")]
    [InlineData(1, "Access")]
    [InlineData(2, "Trunk")]
    [InlineData(3, "Private")]
    [InlineData(9, "Unknown (9)")]
    public void VlanOperationMode_Table(object? raw, string expected) =>
        Assert.Equal(expected, HyperVMapper.MapVlanOperationMode(raw));

    [Fact]
    public void VlanRow_TrunkListJoined()
    {
        var row = HyperVMapper.MapVlanRow("N1", "VM1", "Network Adapter", Bag(
            ("OperationMode", (uint)2),
            ("AccessVlanId", (ushort)0),
            ("NativeVlanId", (ushort)10),
            ("TrunkVlanIdArray", new ushort[] { 10, 20, 30 })));
        Assert.Equal("Trunk", row.OperationMode);
        Assert.Equal(10, row.NativeVlanId);
        Assert.Equal("10, 20, 30", row.AllowedVlanIdList);
    }

    [Fact]
    public void VlanRow_Untagged_EmptyList()
    {
        var row = HyperVMapper.MapVlanRow("N1", "VM1", "NIC", Bag(("OperationMode", (uint)0)));
        Assert.Equal("Untagged", row.OperationMode);
        Assert.Null(row.AccessVlanId);
        Assert.Equal(string.Empty, row.AllowedVlanIdList);
    }

    // ---- Switches ----

    [Theory]
    [InlineData(true, false, "External", false)]
    [InlineData(true, true, "External", true)]
    [InlineData(false, true, "Internal", true)]
    [InlineData(false, false, "Private", false)]
    public void SwitchRow_TypeDerivation(bool external, bool internalPort, string type, bool mgmt)
    {
        var row = HyperVMapper.MapSwitchRow("N1", "SW", external, internalPort, "Intel");
        Assert.Equal(type, row.SwitchType);
        Assert.Equal(mgmt, row.AllowManagementOS);
    }

    // ---- Current-VSSD NIC → switch via allocation (Task 10) ----

    private const string NicVssd = @"Microsoft:BFA881C4-9E3D-4C61-BE95-E20E0A44E239";
    private const string NicAdapter = "AFB9F2E4-401D-4CD8-8764-2B2C9096FE53";
    private const string DefaultSwitchGuid = "C08CB7B8-9B3C-408E-8E30-5E16A3AEB444";
    private const string OtherSwitchGuid = "AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA";

    private static string NicInstanceId => $"{NicVssd}\\{NicAdapter}";

    private static Dictionary<string, object?> NicSetting(string connectionPortGuid = "") => Bag(
        ("__Class", "Msvm_SyntheticEthernetPortSettingData"),
        ("InstanceID", NicInstanceId),
        ("ElementName", "Network Adapter"),
        ("Address", "00155D03B501"),
        ("Connection", string.IsNullOrEmpty(connectionPortGuid) ? new string[] { } : new[] { connectionPortGuid }));

    private static Dictionary<string, object?> PortAllocation(
        string parentNicInstanceId, string hostResource, object? enabledState) => Bag(
        ("__Class", "Msvm_EthernetPortAllocationSettingData"),
        ("InstanceID", $"{NicVssd}\\{NicAdapter}\\C"),
        ("ElementName", "Dynamic Ethernet Switch Port"),
        ("Parent", parentNicInstanceId),
        ("HostResource", hostResource),
        ("EnabledState", enabledState));

    private static string SwitchHostResource(string guid) =>
        $"Msvm_VirtualEthernetSwitch.Name = \"{guid}\"";

    private static Dictionary<string, string> SwitchGuidMap() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultSwitchGuid] = "Default Switch",
            [OtherSwitchGuid] = "Switch B",
        };

    [Fact]
    public void NicSwitch_ActiveAllocationResolvesSwitch()
    {
        var components = new[]
        {
            NicSetting(),
            PortAllocation(NicInstanceId, SwitchHostResource(DefaultSwitchGuid), (ushort)2),
        };
        var nic = Assert.Single(HyperVMapper.SelectCurrentVmNics(
            "DOSGUID", "DOS 6.22", components,
            new Dictionary<string, string>(), new Dictionary<string, string>(), SwitchGuidMap()));
        Assert.Equal("Default Switch", nic.Switch);
    }

    [Fact]
    public void NicSwitch_EmptyConnectionPlusActiveAllocation_StillResolved()
    {
        // Live DOS shape: Connection={} but the current allocation carries
        // the Default Switch HostResource.
        var components = new[]
        {
            NicSetting(),
            PortAllocation(NicInstanceId, SwitchHostResource(DefaultSwitchGuid), (ushort)2),
        };
        var nic = Assert.Single(HyperVMapper.SelectCurrentVmNics(
            "DOSGUID", "DOS 6.22", components,
            new Dictionary<string, string>(), new Dictionary<string, string>(), SwitchGuidMap()));
        Assert.Equal("Default Switch", nic.Switch);
        Assert.Equal("00155D03B501", nic.Mac);
    }

    [Fact]
    public void NicSwitch_DisabledAllocation_Empty()
    {
        var components = new[]
        {
            NicSetting(),
            PortAllocation(NicInstanceId, SwitchHostResource(DefaultSwitchGuid), (ushort)3),
        };
        var nic = Assert.Single(HyperVMapper.SelectCurrentVmNics(
            "DOSGUID", "DOS 6.22", components,
            new Dictionary<string, string>(), new Dictionary<string, string>(), SwitchGuidMap()));
        Assert.Equal(string.Empty, nic.Switch);
    }

    [Fact]
    public void NicSwitch_NoAllocation_NoConnection_Empty()
    {
        var nic = Assert.Single(HyperVMapper.SelectCurrentVmNics(
            "DOSGUID", "DOS 6.22", [NicSetting()],
            new Dictionary<string, string>(), new Dictionary<string, string>(), SwitchGuidMap()));
        Assert.Equal(string.Empty, nic.Switch);
    }

    [Fact]
    public void NicSwitch_HistoricalAllocationIgnored_CurrentWins()
    {
        // A foreign-VSSD allocation for the same adapter must never leak in:
        // only components of the current VSSD are passed, so Switch A wins.
        var current = new[]
        {
            NicSetting(),
            PortAllocation(NicInstanceId, SwitchHostResource(DefaultSwitchGuid), (ushort)2),
        };
        var nic = Assert.Single(HyperVMapper.SelectCurrentVmNics(
            "DOSGUID", "DOS 6.22", current,
            new Dictionary<string, string>(), new Dictionary<string, string>(), SwitchGuidMap()));
        Assert.Equal("Default Switch", nic.Switch);

        // Historical allocation alone (different Parent VSSD path) is not
        // recognized as belonging to this NIC: switch stays "".
        var stale = PortAllocation(
            @"Microsoft:OTHER-VSSD\" + NicAdapter, SwitchHostResource(OtherSwitchGuid), (ushort)2);
        var staleNic = Assert.Single(HyperVMapper.SelectCurrentVmNics(
            "DOSGUID", "DOS 6.22", [NicSetting(), stale],
            new Dictionary<string, string>(), new Dictionary<string, string>(), SwitchGuidMap()));
        Assert.Equal(string.Empty, staleNic.Switch);
    }

    [Fact]
    public void NicSwitch_NoSingleSwitchFallback()
    {
        // Allocation references an UNKNOWN switch GUID: must stay "" even
        // though exactly one known switch exists in the map.
        var components = new[]
        {
            NicSetting(),
            PortAllocation(NicInstanceId, SwitchHostResource("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB"), (ushort)2),
        };
        var nic = Assert.Single(HyperVMapper.SelectCurrentVmNics(
            "DOSGUID", "DOS 6.22", components,
            new Dictionary<string, string>(), new Dictionary<string, string>(), SwitchGuidMap()));
        Assert.Equal(string.Empty, nic.Switch);
    }

    [Theory]
    [InlineData((ushort)2, true)]
    [InlineData((ushort)3, false)]
    [InlineData(null, true)]
    public void AllocationActive_States(object? raw, bool expected)
    {
        var bag = Bag(("EnabledState", raw));
        if (raw is null)
        {
            bag.Remove("EnabledState");
        }

        Assert.Equal(expected, HyperVMapper.IsAllocationActive(bag));
    }

    // ---- Current-VSSD drive selection ----

    private const string DosIdeControllerId =
        @"Microsoft:DOSVM\83F8638B-8DCA-4152-9EDA-2CA8B33039B4\0";
    private const string DosDriveId =
        @"Microsoft:DOSVM\83F8638B-8DCA-4152-9EDA-2CA8B33039B4\0\0\D";
    private const string DosAvhd =
        @"C:\ProgramData\Microsoft\Windows\Virtual Hard Disks\dos_cd_02C239D7-8622-4B78-B6DB-CC80DC6E62C2.avhd";

    private static Dictionary<string, object?> IdeController(string id, int number) => Bag(
        ("__Class", "Msvm_ResourceAllocationSettingData"),
        ("InstanceID", id),
        ("ResourceType", (ushort)5),
        ("ResourceSubType", "Microsoft:Hyper-V:Emulated IDE Controller"),
        ("Address", (ushort)number));

    private static Dictionary<string, object?> ScsiController(string id, int number) => Bag(
        ("__Class", "Msvm_ResourceAllocationSettingData"),
        ("InstanceID", id),
        ("ResourceType", (ushort)6),
        ("ResourceSubType", "Microsoft:Hyper-V:Synthetic SCSI Controller"),
        ("Address", (ushort)number));

    private static Dictionary<string, object?> HardDrive(string id, string parentControllerId, int location) => Bag(
        ("__Class", "Msvm_ResourceAllocationSettingData"),
        ("InstanceID", id),
        ("ResourceType", (ushort)17),
        ("ResourceSubType", "Microsoft:Hyper-V:Synthetic Disk Drive"),
        ("Parent", parentControllerId),
        ("AddressOnParent", (ushort)location));

    private static Dictionary<string, object?> HardDiskImage(string parentDriveId, string path) => Bag(
        ("__Class", "Msvm_StorageAllocationSettingData"),
        ("InstanceID", "Microsoft:Image\\" + Guid.NewGuid()),
        ("ResourceType", (ushort)31),
        ("ResourceSubType", "Microsoft:Hyper-V:Virtual Hard Disk"),
        ("Parent", parentDriveId),
        ("HostResource", new[] { path }));

    private static Dictionary<string, object?> FloppyDrive(string id) => Bag(
        ("__Class", "Msvm_ResourceAllocationSettingData"),
        ("InstanceID", id),
        ("ResourceType", (ushort)14),
        ("ResourceSubType", "Microsoft:Hyper-V:Synthetic Diskette Drive"),
        ("Parent", DosIdeControllerId),
        ("AddressOnParent", (ushort)0));

    private static Dictionary<string, object?> FloppyImage(string parentDriveId, string path) => Bag(
        ("__Class", "Msvm_StorageAllocationSettingData"),
        ("InstanceID", "Microsoft:Image\\" + Guid.NewGuid()),
        ("ResourceType", (ushort)31),
        ("ResourceSubType", "Microsoft:Hyper-V:Virtual Floppy Disk"),
        ("Parent", parentDriveId),
        ("HostResource", new[] { path }));

    private static Dictionary<string, object?> DvdDrive(string id) => Bag(
        ("__Class", "Msvm_ResourceAllocationSettingData"),
        ("InstanceID", id),
        ("ResourceType", (ushort)16),
        ("ResourceSubType", "Microsoft:Hyper-V:Synthetic DVD Drive"),
        ("Parent", DosIdeControllerId),
        ("AddressOnParent", (ushort)1));

    private static Dictionary<string, object?> DvdImage(string parentDriveId, string path) => Bag(
        ("__Class", "Msvm_StorageAllocationSettingData"),
        ("InstanceID", "Microsoft:Image\\" + Guid.NewGuid()),
        ("ResourceType", (ushort)31),
        ("ResourceSubType", "Microsoft:Hyper-V:Virtual CD/DVD Disk"),
        ("Parent", parentDriveId),
        ("HostResource", new[] { path }));

    [Fact]
    public void Drives_ControllerFormat_Scsi()
    {
        const string ctrl = @"Microsoft:VM\CTRL-SCSI0";
        const string drive1 = @"Microsoft:VM\CTRL-SCSI0\0\D1";
        const string drive2 = @"Microsoft:VM\CTRL-SCSI0\0\D2";
        var components = new[]
        {
            ScsiController(ctrl, 0),
            HardDrive(drive1, ctrl, 1),
            HardDiskImage(drive1, @"C:\VMs\d.vhdx"),
            HardDrive(drive2, ctrl, 0),
            HardDiskImage(drive2, @"C:\VMs\e.vhdx"),
        };
        var drives = HyperVMapper.SelectCurrentVmDrives(components);
        Assert.Equal(2, drives.Count);
        Assert.Contains(drives, d => d.Controller == "SCSI 0:1" && d.Path == @"C:\VMs\d.vhdx");
        Assert.Contains(drives, d => d.Controller == "SCSI 0:0");
    }

    [Fact]
    public void Drives_RealDosGraph_ExactlyOneIdeRow_VfdAndIsoExcluded()
    {
        const string floppyId = @"Microsoft:DOSVM\FLOPPY0";
        const string dvdId = @"Microsoft:DOSVM\DVD0";
        var components = new[]
        {
            IdeController(DosIdeControllerId, 0),
            HardDrive(DosDriveId, DosIdeControllerId, 0),
            HardDiskImage(DosDriveId, DosAvhd),
            FloppyDrive(floppyId),
            FloppyImage(floppyId, @"C:\Users\Pivan\Downloads\Microsoft Mouse 8.20.vfd"),
            DvdDrive(dvdId),
            DvdImage(dvdId, @"C:\Users\Pivan\Downloads\MS-DOS 6.22.iso"),
        };
        var drives = Assert.Single(HyperVMapper.SelectCurrentVmDrives(components).ToList());
        Assert.Equal("IDE 0:0", drives.Controller);
        Assert.Equal(DosAvhd, drives.Path);
    }

    [Fact]
    public void Drives_DriveWithoutImage_NoRow_MissingControllerUnknown()
    {
        // A Hard Drive with no Virtual-Hard-Disk child is not a hard disk
        // (Get-VMHardDiskDrive parity); an image whose controller is unknown
        // keeps Unknown numbers rather than failing the VM.
        var orphan = HardDrive(@"Microsoft:VM\ORPHAN", @"Microsoft:VM\GONE", 3);
        var image = HardDiskImage(@"Microsoft:VM\ORPHAN", @"C:\VMs\d.vhdx");
        var drives = Assert.Single(HyperVMapper.SelectCurrentVmDrives([orphan, image]).ToList());
        Assert.Equal("Unknown 0:3", drives.Controller);

        var bare = HardDrive(@"Microsoft:VM\BARE", @"Microsoft:VM\CTRL", 0);
        Assert.Empty(HyperVMapper.SelectCurrentVmDrives([bare]));
    }

    [Fact]
    public void Drives_EmptyHostResource_KeepsRowForUnknownSemantics()
    {
        const string ctrl = @"Microsoft:VM\CTRL-IDE0";
        const string drive = @"Microsoft:VM\CTRL-IDE0\0\D";
        var image = HardDiskImage(drive, string.Empty);
        image["HostResource"] = new string[] { };
        var components = new[] { IdeController(ctrl, 0), HardDrive(drive, ctrl, 0), image };
        // A drive whose image carries no HostResource still yields a row
        // (empty path); VHD inspection then fails into Unknown downstream.
        var drives = HyperVMapper.SelectCurrentVmDrives(components);
        var row = Assert.Single(drives.ToList());
        Assert.Equal("IDE 0:0", row.Controller);
        Assert.Equal(string.Empty, row.Path);
    }

    // ---- VHD setting-data parser ----

    private const string SampleMof = """
        instance of Msvm_VirtualHardDiskSettingData
        {
            Caption = "Virtual Hard Disk Setting Data";
            Path = "C:\\VMs\\DOS622.vhdx";
            Type = 3;
            Format = 3;
            MaxInternalSize = 68719476736;
        };
        """;

    [Fact]
    public void VhdParser_SampleMof()
    {
        var parsed = VhdSettingDataParser.Parse(SampleMof);
        Assert.Equal(3, parsed.Type);
        Assert.Equal(3, parsed.Format);
        Assert.Equal(68719476736UL, parsed.MaxInternalSize);
        Assert.Equal(@"C:\VMs\DOS622.vhdx", parsed.Path);
        Assert.Equal("Dynamic", VhdSettingDataParser.MapVhdType(parsed.Type));
        Assert.Equal("VHDX", VhdSettingDataParser.MapVhdFormat(parsed.Format));
    }

    [Theory]
    [InlineData(2, "Fixed")]
    [InlineData(3, "Dynamic")]
    [InlineData(4, "Differencing")]
    [InlineData(9, "Unknown")]
    [InlineData(null, "Unknown")]
    public void VhdType_Table(int? raw, string expected) =>
        Assert.Equal(expected, VhdSettingDataParser.MapVhdType(raw));

    [Theory]
    [InlineData(2, "VHD")]
    [InlineData(3, "VHDX")]
    [InlineData(4, "VHDSet")]
    [InlineData(9, "Unknown")]
    [InlineData(null, "Unknown")]
    public void VhdFormat_Table(int? raw, string expected) =>
        Assert.Equal(expected, VhdSettingDataParser.MapVhdFormat(raw));

    [Fact]
    public void VhdParser_Empty_IsUnknown() =>
        Assert.Equal("Unknown", VhdSettingDataParser.MapVhdType(VhdSettingDataParser.Parse(null).Type));

    // ---- Real provider CIM-XML shape (Task 12) ----

    private const string RealCimXml = """
        <INSTANCE CLASSNAME="Msvm_VirtualHardDiskSettingData">
          <PROPERTY NAME="Caption" TYPE="string"><VALUE>Virtual Hard Disk Setting Data</VALUE></PROPERTY>
          <PROPERTY NAME="Format" TYPE="uint16"><VALUE>2</VALUE></PROPERTY>
          <PROPERTY NAME="MaxInternalSize" TYPE="uint64"><VALUE>2147483648</VALUE></PROPERTY>
          <PROPERTY NAME="ParentPath" TYPE="string"><VALUE>C:\ProgramData\Microsoft\Windows\Virtual Hard Disks\dos_cd.vhd</VALUE></PROPERTY>
          <PROPERTY NAME="Path" TYPE="string"><VALUE>C:\ProgramData\Microsoft\Windows\Virtual Hard Disks\dos_cd_02C239D7-8622-4B78-B6DB-CC80DC6E62C2.avhd</VALUE></PROPERTY>
          <PROPERTY NAME="Type" TYPE="uint16"><VALUE>4</VALUE></PROPERTY>
        </INSTANCE>
        """;

    [Fact]
    public void VhdParser_RealCimXml()
    {
        var parsed = VhdSettingDataParser.Parse(RealCimXml);
        Assert.Equal(4, parsed.Type);
        Assert.Equal(2, parsed.Format);
        Assert.Equal(2147483648UL, parsed.MaxInternalSize);
        Assert.Equal(
            @"C:\ProgramData\Microsoft\Windows\Virtual Hard Disks\dos_cd_02C239D7-8622-4B78-B6DB-CC80DC6E62C2.avhd",
            parsed.Path);
        Assert.Equal("Differencing", VhdSettingDataParser.MapVhdType(parsed.Type));
        Assert.Equal("VHD", VhdSettingDataParser.MapVhdFormat(parsed.Format));
    }

    [Fact]
    public void VhdParser_MofFallback_StillWorks() =>
        Assert.Equal("Dynamic", VhdSettingDataParser.MapVhdType(VhdSettingDataParser.Parse(SampleMof).Type));

    [Fact]
    public void BytesToGigabytes_TinyFile_IsZeroNotMissing()
    {
        // Get-VHD FileSize 81920 bytes => 0.0 GB: numeric zero must survive
        // as 0.0 (present), never collapse into null/empty.
        Assert.Equal(0.0, Pi1.HyperVToolkit.Infrastructure.Mapping.CimValues.BytesToGigabytes(81920));
        Assert.Equal(2.0, Pi1.HyperVToolkit.Infrastructure.Mapping.CimValues.BytesToGigabytes(2147483648UL));
    }
}
