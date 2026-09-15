using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Services;

namespace Pi1.HyperVToolkit.Core.State;

/// <summary>Latest capability results. Single-writer (shell), multi-reader.</summary>
public sealed class CapabilitySnapshot : ICapabilitySnapshot
{
    private readonly object _sync = new();
    private CapabilityReport? _hyperV;
    private CapabilityReport? _cluster;
    private CapabilityReport? _cim;

    public CapabilityReport? HyperV
    {
        get { lock (_sync) { return _hyperV; } }
    }

    public CapabilityReport? Cluster
    {
        get { lock (_sync) { return _cluster; } }
    }

    public CapabilityReport? Cim
    {
        get { lock (_sync) { return _cim; } }
    }

    public bool IsClusterAvailable => Cluster?.IsAvailable == true;
    public bool IsHyperVAvailable => HyperV?.IsAvailable == true;

    public void Update(CapabilityReport? hyperV, CapabilityReport? cluster, CapabilityReport? cim)
    {
        lock (_sync)
        {
            _hyperV = hyperV;
            _cluster = cluster;
            _cim = cim;
        }
    }
}
