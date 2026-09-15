namespace Pi1.HyperVToolkit.Core.Models;

/// <summary>
/// Status/severity tokens. Parity with the PowerShell Write-PiTable colour rules,
/// which classify cell values into OK / Info / Warning / Critical (+ None / N/A).
/// The English tokens are preserved as canonical values; Slovak UI chrome
/// is added around them, never instead of them.
/// </summary>
public enum Severity
{
    Ok = 0,
    Info = 1,
    Warning = 2,
    Critical = 3,

    /// <summary>PowerShell "None" (e.g. no storage jobs — a good state).</summary>
    None = 4,

    /// <summary>PowerShell "N/A" (e.g. no cluster present).</summary>
    NotApplicable = 5,
}

/// <summary>Canonical display tokens, identical to the PowerShell table values.</summary>
public static class SeverityTokens
{
    public static string ToToken(this Severity severity) => severity switch
    {
        Severity.Ok => "OK",
        Severity.Info => "Info",
        Severity.Warning => "Warning",
        Severity.Critical => "Critical",
        Severity.None => "None",
        Severity.NotApplicable => "N/A",
        _ => severity.ToString(),
    };
}
