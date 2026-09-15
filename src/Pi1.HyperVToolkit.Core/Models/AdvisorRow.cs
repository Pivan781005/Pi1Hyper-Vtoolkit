namespace Pi1.HyperVToolkit.Core.Models;

// Semantic rule behind one Advisor finding. Parity with Show-PiAdvisor
// (Pi1.Diagnostics.psm1): WasteGB < 0 => Pressure (Warning); WasteGB > 16 =>
// LargeReserve (Info); WasteGB > 8 => Reserve (Info). Sensitive-workload
// names only override the Info recommendation text, never the severity.
public enum AdvisorRule
{
    Pressure = 0,
    LargeReserve = 1,
    Reserve = 2,
}

// One Advisor finding. Parity with the PowerShell row fields:
// Severity, HostNode, VM, AssignedGB, DemandGB, WasteGB, Recommendation.
// Core carries the semantic Rule + IsSensitive flag; WPF/localization renders
// the Slovak/English recommendation prose (no language in Core).
// Identifiers stay canonical; numeric GB values are the already-rounded
// provider values (no recompute).
public sealed record AdvisorRow(
    string Severity,
    string HostNode,
    string VM,
    double AssignedGB,
    double DemandGB,
    double WasteGB,
    AdvisorRule Rule,
    bool IsSensitive);
