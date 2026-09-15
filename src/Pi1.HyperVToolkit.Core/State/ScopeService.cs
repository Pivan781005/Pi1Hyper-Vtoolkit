using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Services;

namespace Pi1.HyperVToolkit.Core.State;

/// <summary>Single in-memory owner of the read scope. Thread-safe for read-mostly UI use.</summary>
public sealed class ScopeService : IScopeService
{
    private readonly object _sync = new();
    private ScopeState _current;

    public ScopeService(string? machineName = null)
    {
        MachineName = string.IsNullOrWhiteSpace(machineName)
            ? Environment.MachineName
            : machineName;
        _current = new ScopeState(ScopeMode.Local, MachineName);
    }

    public string MachineName { get; }

    public ScopeState Current
    {
        get { lock (_sync) { return _current; } }
    }

    public bool ClusterEnumerationRequired => Current.Mode == ScopeMode.Cluster;

    /// <summary>
    /// Slovak user-facing scope label. Internal enum/value names stay English.
    /// (Phase 3: English tokens "Local node / All cluster nodes / Selected node"
    /// were replaced with Slovak to match the Slovak UI.)
    /// </summary>
    public string GetScopeLabel()
    {
        var current = Current;
        return current.Mode switch
        {
            ScopeMode.Local => $"Lokálny uzol ({MachineName})",
            ScopeMode.Cluster => "Všetky uzly klastra",
            ScopeMode.Node => $"Vybraný uzol ({current.SelectedNode})",
            _ => $"Lokálny uzol ({MachineName})",
        };
    }

    public void SetScope(ScopeMode mode, string? selectedNode = null)
    {
        ScopeState next = mode switch
        {
            ScopeMode.Local => new ScopeState(ScopeMode.Local, MachineName),
            ScopeMode.Cluster => new ScopeState(ScopeMode.Cluster, Current.SelectedNode),
            ScopeMode.Node => string.IsNullOrWhiteSpace(selectedNode)
                ? throw new ArgumentException(LocalizationService.Instance["Scope_NodeNameRequired"], nameof(selectedNode))
                : new ScopeState(ScopeMode.Node, selectedNode.Trim()),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        lock (_sync)
        {
            _current = next;
        }

        ScopeChanged?.Invoke(this, next);
    }

    public bool TryGetStaticTargetNodes(out IReadOnlyList<string> nodes)
    {
        var current = Current;
        switch (current.Mode)
        {
            case ScopeMode.Local:
                nodes = [MachineName];
                return true;
            case ScopeMode.Node:
                nodes = [current.SelectedNode];
                return true;
            default:
                nodes = [];
                return false;
        }
    }

    public event EventHandler<ScopeState>? ScopeChanged;
}
