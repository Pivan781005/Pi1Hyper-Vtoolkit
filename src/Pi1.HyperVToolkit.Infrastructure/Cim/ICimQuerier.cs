namespace Pi1.HyperVToolkit.Infrastructure.Cim;

/// <summary>
/// Single CIM/WMI query boundary. Implementations speak native CIM
/// (Microsoft.Management.Infrastructure); tests substitute a fake.
/// Property bags use CIM property names; values are raw (string, numeric,
/// bool, DateTime, or arrays thereof).
/// </summary>
public interface ICimQuerier
{
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
        string node,
        string @namespace,
        string wql,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invokes a WMI method on the singleton instance of
    /// <paramref name="className"/> (e.g. Msvm_ImageManagementService).
    /// Returns out-parameters plus "ReturnValue". Used for read-only
    /// inspection methods only (GetVirtualHardDiskSettingData); never for
    /// mutating calls.
    /// </summary>
    Task<IReadOnlyDictionary<string, object?>> InvokeSingletonMethodAsync(
        string node,
        string @namespace,
        string className,
        string methodName,
        IReadOnlyDictionary<string, object?> inParameters,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
