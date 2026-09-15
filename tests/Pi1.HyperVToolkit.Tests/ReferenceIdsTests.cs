using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Management.Infrastructure;
using Pi1.HyperVToolkit.Infrastructure.Cim;
using Pi1.HyperVToolkit.Infrastructure.Mapping;
using Xunit.Abstractions;

namespace Pi1.HyperVToolkit.Tests;

public sealed class ReferenceIdsTests
{
    private readonly ITestOutputHelper _output;

    public ReferenceIdsTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static Dictionary<string, object?> Bag(params (string Key, object? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

    private const string VssdPrefix = @"Microsoft:BFA881C4-9E3D-4C61-BE95-E20E0A44E239";
    private const string ControllerId = VssdPrefix + @"\83F8638B-8DCA-4152-9EDA-2CA8B33039B4\0";
    private const string DriveId = ControllerId + @"\0\D";
    private const string ImageId = ControllerId + @"\0\L";
    private const string DosAvhd =
        @"C:\ProgramData\Microsoft\Windows\Virtual Hard Disks\dos_cd_02C239D7-8622-4B78-B6DB-CC80DC6E62C2.avhd";

    private const string DriveParentRef =
        @"\\LATITUDE-5590\root\virtualization\v2:Msvm_ResourceAllocationSettingData.InstanceID=" +
        @"""Microsoft:BFA881C4-9E3D-4C61-BE95-E20E0A44E239\\83F8638B-8DCA-4152-9EDA-2CA8B33039B4\\0""";
    private const string ImageParentRef =
        @"\\LATITUDE-5590\root\virtualization\v2:Msvm_ResourceAllocationSettingData.InstanceID=" +
        @"""Microsoft:BFA881C4-9E3D-4C61-BE95-E20E0A44E239\\83F8638B-8DCA-4152-9EDA-2CA8B33039B4\\0\\0\\D""";

    [Fact]
    public void ParentReferences_PlainInstanceId() =>
        Assert.True(HyperVMapper.ParentReferences(ControllerId, ControllerId));

    [Fact]
    public void ParentReferences_WmiReferencePath() =>
        Assert.True(HyperVMapper.ParentReferences(DriveParentRef, ControllerId));

    [Fact]
    public void ParentReferences_WmiReferencePath_UnescapesBackslashes()
    {
        Assert.Equal(ControllerId, ReferenceIds.NormalizeReferenceInstanceId(DriveParentRef));
        Assert.Equal(DriveId, ReferenceIds.NormalizeReferenceInstanceId(ImageParentRef));
    }

    [Fact]
    public void ParentReferences_DifferentInstanceId_False()
    {
        // A genuinely different controller (different trailing segment) must
        // not match, even though it shares the VSSD prefix GUIDs.
        const string otherController = VssdPrefix + @"\99F8638B-8DCA-4152-9EDA-2CA8B33039B4\1";
        Assert.False(HyperVMapper.ParentReferences(DriveParentRef, otherController));
        Assert.False(HyperVMapper.ParentReferences(otherController, ControllerId));
    }

    [Fact]
    public void ParentReferences_NoGuidOverlapFallback()
    {
        // Floppy drive shares the VM GUID but is unrelated: must be false.
        const string floppyId = VssdPrefix + @"\FLOPPY0";
        Assert.False(HyperVMapper.ParentReferences(floppyId, ControllerId));
        Assert.False(HyperVMapper.ParentReferences(DriveParentRef, floppyId));
    }

    private static List<Dictionary<string, object?>> RealDosComponents() =>
    [
        Bag(
            ("__Class", "Msvm_ResourceAllocationSettingData"),
            ("ElementName", "IDE Controller 0"),
            ("InstanceID", ControllerId),
            ("ResourceType", (ushort)5),
            ("ResourceSubType", "Microsoft:Hyper-V:Emulated IDE Controller"),
            ("Address", (ushort)0)),
        Bag(
            ("__Class", "Msvm_ResourceAllocationSettingData"),
            ("ElementName", "Hard Drive"),
            ("InstanceID", DriveId),
            ("ResourceType", (ushort)17),
            ("ResourceSubType", "Microsoft:Hyper-V:Synthetic Disk Drive"),
            ("Parent", DriveParentRef),
            ("AddressOnParent", (ushort)0)),
        Bag(
            ("__Class", "Msvm_StorageAllocationSettingData"),
            ("ElementName", "Hard Disk Image"),
            ("InstanceID", ImageId),
            ("ResourceType", (ushort)31),
            ("ResourceSubType", "Microsoft:Hyper-V:Virtual Hard Disk"),
            ("Parent", ImageParentRef),
            ("HostResource", new[] { DosAvhd })),
        Bag(
            ("__Class", "Msvm_ResourceAllocationSettingData"),
            ("ElementName", "Diskette Drive"),
            ("InstanceID", VssdPrefix + @"\FLOPPY0"),
            ("ResourceType", (ushort)14),
            ("ResourceSubType", "Microsoft:Hyper-V:Synthetic Diskette Drive"),
            ("Parent", ControllerId),
            ("AddressOnParent", (ushort)0)),
        Bag(
            ("__Class", "Msvm_StorageAllocationSettingData"),
            ("ElementName", "Floppy Disk Image"),
            ("InstanceID", VssdPrefix + @"\FLOPPY0\F"),
            ("ResourceType", (ushort)31),
            ("ResourceSubType", "Microsoft:Hyper-V:Virtual Floppy Disk"),
            ("Parent", VssdPrefix + @"\FLOPPY0"),
            ("HostResource", new[] { @"C:\Users\Pivan\Downloads\Microsoft Mouse 8.20.vfd" })),
        Bag(
            ("__Class", "Msvm_ResourceAllocationSettingData"),
            ("ElementName", "DVD Drive"),
            ("InstanceID", VssdPrefix + @"\DVD0"),
            ("ResourceType", (ushort)16),
            ("ResourceSubType", "Microsoft:Hyper-V:Synthetic DVD Drive"),
            ("Parent", ControllerId),
            ("AddressOnParent", (ushort)1)),
        Bag(
            ("__Class", "Msvm_StorageAllocationSettingData"),
            ("ElementName", "DVD Image"),
            ("InstanceID", VssdPrefix + @"\DVD0\I"),
            ("ResourceType", (ushort)31),
            ("ResourceSubType", "Microsoft:Hyper-V:Virtual CD/DVD Disk"),
            ("Parent", VssdPrefix + @"\DVD0"),
            ("HostResource", new[] { @"C:\Users\Pivan\Downloads\MS-DOS 6.22.iso" })),
    ];

    [Fact]
    public void RealDosGraph_WmiReferenceParents_ExactlyOneDisk()
    {
        var drives = HyperVMapper.SelectCurrentVmDrives(RealDosComponents());
        Assert.Single(drives);
    }

    [Fact]
    public void RealDosGraph_Controller_Is_IDE_0_0() =>
        Assert.Equal("IDE 0:0", HyperVMapper.SelectCurrentVmDrives(RealDosComponents())[0].Controller);

    [Fact]
    public void RealDosGraph_Path_Is_ExactAvhd() =>
        Assert.Equal(DosAvhd, HyperVMapper.SelectCurrentVmDrives(RealDosComponents())[0].Path);

    [Fact]
    public void RealDosGraph_FloppyVfdExcluded() =>
        Assert.DoesNotContain(
            HyperVMapper.SelectCurrentVmDrives(RealDosComponents()),
            d => d.Path.EndsWith(".vfd", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void RealDosGraph_DvdIsoExcluded() =>
        Assert.DoesNotContain(
            HyperVMapper.SelectCurrentVmDrives(RealDosComponents()),
            d => d.Path.EndsWith(".iso", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void ReferenceInstanceId_CimInstanceObject_PreferredOverText()
    {
        // A live CIM reference arrives as an object, not text: the InstanceID
        // key must be extracted directly (Convert.ToString is NOT sufficient).
        var instance = new CimInstance("Msvm_ResourceAllocationSettingData");
        instance.CimInstanceProperties.Add(CimProperty.Create("InstanceID", ControllerId, CimType.String, CimFlags.Key));
        var extracted = ReferenceIds.TryGetReferencedInstanceId(instance);
        Assert.Equal(ControllerId, extracted);
        Assert.True(HyperVMapper.ParentReferencesValue(instance, ControllerId));
    }

    [Fact]
    public async Task InspectReferenceRuntimeType_LiveAssociation()
    {
        // Deliberate inspection of what MMI returns for reference-typed CIM
        // properties (uses an unelevated-readable root\\cimv2 association).
        using var querier = new MmiCimQuerier(NullLogger<MmiCimQuerier>.Instance);
        var rows = await querier.QueryAsync(
            ".", @"root\cimv2",
            "SELECT Antecedent, Dependent FROM Win32_LogicalDiskToPartition",
            TimeSpan.FromSeconds(30));
        Assert.NotEmpty(rows);
        var raw = rows[0]["Antecedent"];
        _output.WriteLine($"Antecedent runtime type: {raw?.GetType().FullName ?? "<null>"}");
        Assert.True(raw is CimInstance || raw is string, $"Unhandled reference shape: {raw?.GetType().FullName}");
        var id = ReferenceIds.TryGetReferencedInstanceId(raw);
        _output.WriteLine($"Extracted identity: {id}");
        Assert.False(string.IsNullOrEmpty(id));
    }
}
