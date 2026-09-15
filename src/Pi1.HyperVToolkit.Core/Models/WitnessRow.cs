namespace Pi1.HyperVToolkit.Core.Models;

// Parity with Show-PiWitness rows: one Quorum section row plus one row per
// resource whose type matches "Witness" or whose name matches
// "Witness|Quorum". Detail carries the intended parameter subset
// (Share|Path|Account|Endpoint|Witness|Disk|Cloud|Storage|File as Name=Value,
// "; "-joined); empty when parameters are unavailable (PARTIAL).
public sealed record WitnessRow(
    string Section,
    string Name,
    string Type,
    string State,
    string OwnerGroup,
    string OwnerNode,
    string Detail);
