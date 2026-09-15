namespace Pi1.HyperVToolkit.Infrastructure.HyperV;

/// <summary>
/// WQL ASSOCIATORS OF builders for authoritative Hyper-V relationships.
/// Pure string construction — fully unit-testable; executed through the
/// existing <see cref="Cim.ICimQuerier"/> (WQL supports ASSOCIATORS OF),
/// so no new query abstraction is required.
/// </summary>
public static class AssociationQueries
{
    /// <summary>Current (realized) virtual system settings; snapshots differ.</summary>
    public const string RealizedSystemType = "Microsoft:Hyper-V:System:Realized";

    public const string SettingDataComponentAssoc = "Msvm_VirtualSystemSettingDataComponent";
    public const string SwitchPortResultClass = "Msvm_EthernetSwitchPort";

    public static readonly IReadOnlySet<string> EthernetSettingClasses =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Msvm_SyntheticEthernetPortSettingData",
            "Msvm_EmulatedEthernetPortSettingData",
        };

    /// <summary>Components (memory, CPU, NICs, …) of one VSSD instance.</summary>
    public static string ComponentsOfSetting(string vssdInstanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vssdInstanceId);
        return $"ASSOCIATORS OF {{Msvm_VirtualSystemSettingData.InstanceID=\"{vssdInstanceId}\"}} " +
            $"WHERE AssocClass = {SettingDataComponentAssoc}";
    }

    /// <summary>Switch ports hosted by one virtual switch (port GUID → switch).</summary>
    public static string PortsOfSwitch(string switchGuid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(switchGuid);
        return $"ASSOCIATORS OF {{Msvm_VirtualEthernetSwitch.CreationClassName=\"Msvm_VirtualEthernetSwitch\"," +
            $"Name=\"{switchGuid}\"}} WHERE ResultClass = {SwitchPortResultClass}";
    }

    /// <summary>External uplink ports bound to one virtual switch.</summary>
    public static string ExternalPortsOfSwitch(string switchGuid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(switchGuid);
        return $"ASSOCIATORS OF {{Msvm_VirtualEthernetSwitch.CreationClassName=\"Msvm_VirtualEthernetSwitch\"," +
            $"Name=\"{switchGuid}\"}} WHERE ResultClass = Msvm_ExternalEthernetPort";
    }

    /// <summary>Host (management OS) ports on one virtual switch.</summary>
    public static string InternalPortsOfSwitch(string switchGuid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(switchGuid);
        return $"ASSOCIATORS OF {{Msvm_VirtualEthernetSwitch.CreationClassName=\"Msvm_VirtualEthernetSwitch\"," +
            $"Name=\"{switchGuid}\"}} WHERE ResultClass = Msvm_InternalEthernetPort";
    }
}
