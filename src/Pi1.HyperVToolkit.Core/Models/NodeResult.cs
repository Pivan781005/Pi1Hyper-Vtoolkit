namespace Pi1.HyperVToolkit.Core.Models;

/// <summary>Per-node outcome of a fanned-out query. Either data or an error, never both.</summary>
public sealed record NodeResult<T>(string Node, bool IsSuccess, T? Data, NodeError? Error)
{
    public static NodeResult<T> Ok(string node, T data) => new(node, true, data, null);

    public static NodeResult<T> Fail(string node, NodeError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(node, false, default, error);
    }
}
