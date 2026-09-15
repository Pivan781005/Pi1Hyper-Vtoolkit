using Pi1.HyperVToolkit.Core.Models;

namespace Pi1.HyperVToolkit.Infrastructure.Common;

/// <summary>
/// Maps raw query exceptions to classified node errors. The stored Message is
/// fixed legacy text; user-visible rendering must use NodeErrorText.For(Kind,
/// node), which follows the application SK/EN selection. Detail always keeps
/// the original provider/system text. Cancellation is never mapped here —
/// callers let OperationCanceledException propagate when the caller's own
/// token was cancelled.
/// </summary>
public static class NodeErrorMapper
{
    public static NodeError Map(string node, Exception exception, bool hyperVNamespace = false)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var message = exception.Message ?? string.Empty;

        if (exception is UnauthorizedAccessException)
        {
            return new NodeError(NodeErrorKind.AccessDenied, $"Prístup odmietnutý ({node}).", message);
        }

        if (exception is TimeoutException)
        {
            return new NodeError(NodeErrorKind.Timeout, $"Časový limit vypršal ({node}).", message);
        }

        if (ContainsAny(message, "0x800706BA", "0x800706BF", "RPC server is unavailable", "RPC server je nedostupný",
                "0x80072EEU", "0x80072EE7", "no such host", "could not be resolved", "0x0000274C", "0x00002751"))
        {
            return new NodeError(NodeErrorKind.HostUnreachable, $"Hostiteľ je nedostupný ({node}).", message);
        }

        if (ContainsAny(message, "0x80070005", "access is denied", "prístup bol odmietnutý"))
        {
            return new NodeError(NodeErrorKind.AccessDenied, $"Prístup odmietnutý ({node}).", message);
        }

        if (hyperVNamespace && ContainsAny(message, "0x8004100E", "0x80041002", "invalid namespace", "not found", "nenájdený"))
        {
            return new NodeError(NodeErrorKind.HyperVUnavailable, $"Hyper-V nie je na uzle dostupné ({node}).", message);
        }

        if (ContainsAny(message, "timed out", "timeout", "vypršal"))
        {
            return new NodeError(NodeErrorKind.Timeout, $"Časový limit vypršal ({node}).", message);
        }

        if (ContainsAny(message, "0x800410", "WMI", "CIM"))
        {
            return new NodeError(NodeErrorKind.WmiUnavailable, $"CIM/WMI dotaz zlyhal ({node}).", message);
        }

        return new NodeError(NodeErrorKind.Unknown, $"Dotaz zlyhal ({node}).", message);
    }

    private static bool ContainsAny(string message, params string[] fragments) =>
        fragments.Any(f => message.Contains(f, StringComparison.OrdinalIgnoreCase));
}
