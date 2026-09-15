using Microsoft.Extensions.Logging;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Services;
using Pi1.HyperVToolkit.Infrastructure.Cim;
using Pi1.HyperVToolkit.Infrastructure.Common;
using Pi1.HyperVToolkit.Infrastructure.Mapping;

namespace Pi1.HyperVToolkit.Infrastructure.Scope;

/// <summary>
/// Single scope-resolution mechanism for all views.
/// Local resolves statically; Node resolves to the selected node; Cluster
/// enumerates MSCluster_Node names when the capability snapshot allows it.
/// Full cluster inventory stays a later phase; only node names come from
/// root/MSCluster here.
/// </summary>
public sealed class TargetNodeResolver : ITargetNodeResolver
{
    private readonly IScopeService _scope;
    private readonly ICapabilitySnapshot _capabilities;
    private readonly ICimQuerier _cim;
    private readonly ILogger<TargetNodeResolver> _logger;

    public TargetNodeResolver(
        IScopeService scope,
        ICapabilitySnapshot capabilities,
        ICimQuerier cim,
        ILogger<TargetNodeResolver> logger)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _cim = cim ?? throw new ArgumentNullException(nameof(cim));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<TargetNodeSet> ResolveAsync(ScopeState scope, CancellationToken cancellationToken = default)
    {
        switch (scope.Mode)
        {
            case ScopeMode.Local:
                return new TargetNodeSet([_scope.MachineName], []);
            case ScopeMode.Node:
                if (string.IsNullOrWhiteSpace(scope.SelectedNode))
                {
                    return new TargetNodeSet([], [Core.State.LocalizationService.Instance["Scope_NoNodeSelected"]]);
                }

                return new TargetNodeSet([scope.SelectedNode], []);
            case ScopeMode.Cluster:
                return await ResolveClusterAsync(cancellationToken).ConfigureAwait(false);
            default:
                return new TargetNodeSet([_scope.MachineName], []);
        }
    }

    private async Task<TargetNodeSet> ResolveClusterAsync(CancellationToken cancellationToken)
    {
        if (!_capabilities.IsClusterAvailable)
        {
            return new TargetNodeSet([], [Core.State.LocalizationService.Instance["Scope_ClusterUnavailable"]]);
        }

        try
        {
            var rows = await _cim.QueryAsync(
                _scope.MachineName, @"root\MSCluster",
                "SELECT Name FROM MSCluster_Node", CimTimeouts.Query, cancellationToken).ConfigureAwait(false);
            var nodes = rows
                .Select(r => CimValues.GetString(r, "Name"))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (nodes.Count == 0)
            {
                return new TargetNodeSet([], [Core.State.LocalizationService.Instance["Scope_NoClusterNodes"]]);
            }

            return new TargetNodeSet(nodes, []);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Načítanie uzlov klastra zlyhalo.");
            var error = NodeErrorMapper.Map(_scope.MachineName, ex);
            // Localized kind text + raw provider detail (Detail stays the
            // original system text; only our own message is localized).
            var cause = string.IsNullOrWhiteSpace(error.Detail)
                ? Core.Localization.NodeErrorText.For(error.Kind, _scope.MachineName)
                : $"{Core.Localization.NodeErrorText.For(error.Kind, _scope.MachineName)} {error.Detail}";
            return new TargetNodeSet([], [string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                Core.State.LocalizationService.Instance["Scope_LoadNodesFailed"],
                cause)]);
        }
    }
}
