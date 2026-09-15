namespace Pi1.HyperVToolkit.Core.Services;

// Deterministic in-memory session cache for already collected immutable
// snapshots. Keyed by (dataset, scopeKey) so Local / Cluster / Selected Node
// data can never contaminate each other. Process lifetime only: no disk,
// no time-based expiry, no cross-launch state.
public interface ISessionCache
{
    bool TryGet<T>(string dataset, string scopeKey, out IReadOnlyList<T> rows);

    bool TryGetWarnings(string dataset, string scopeKey, out IReadOnlyList<string> warnings);

    // Stores a SUCCESSFULLY collected snapshot. Failures never overwrite a
    // valid entry (no poisoning); unrelated datasets are untouched.
    void Set<T>(string dataset, string scopeKey, IReadOnlyList<T> rows, IReadOnlyList<string>? warnings = null);

    // Clears one dataset, one scope, or everything (both null).
    void Invalidate(string? dataset = null, string? scopeKey = null);
}

// Dataset names and scope-key construction shared by every view model.
public static class SessionCacheKeys
{
    public const string LocalScopeKey = "local";

    public const string VmBase = "VmBase";
    public const string VmNetwork = "VmNetwork";
    public const string Checkpoints = "Checkpoints";
    public const string Hardware = "Hardware";
    public const string NodeVolumes = "NodeVolumes";
    public const string NodeAdapters = "NodeAdapters";
    public const string HostSettings = "HostSettings";
    public const string StorageJobs = "StorageJobs";
    public const string StoragePools = "StoragePools";
    public const string VirtualDisks = "VirtualDisks";
    public const string PhysicalDisks = "PhysicalDisks";
    public const string StorageVolumes = "StorageVolumes";
    public const string Csv = "Csv";
    public const string VmStorage = "VmStorage";
    public const string Switches = "Switches";
    public const string Vlans = "Vlans";
    public const string Cluster = "Cluster";

    // Cluster-wide snapshot is stored as a single-item list under this key;
    // cluster topology does not depend on VM scope filtering.
    public const string ClusterSnapshot = "ClusterSnapshot";
    public const string ClusterScopeKey = "cluster";

    public static string ScopeKey(string mode, IEnumerable<string> targetNodes) =>
        $"{mode}|{string.Join(",", targetNodes.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))}";
}
