namespace Pi1.HyperVToolkit.Core.Models;

/// <summary>
/// Data-read scope. Parity with PowerShell $Script:ScopeMode ("Local" / "Cluster" / "Node").
/// Single source of truth lives in <see cref="Services.IScopeService"/>.
/// </summary>
public enum ScopeMode
{
    /// <summary>Local node (Environment.MachineName).</summary>
    Local = 0,

    /// <summary>All cluster nodes. Requires Failover Cluster capability.</summary>
    Cluster = 1,

    /// <summary>One explicitly selected node.</summary>
    Node = 2,
}
