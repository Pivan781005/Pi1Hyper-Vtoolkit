using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.State;

namespace Pi1.HyperVToolkit.Core.Localization;

// Presentation-side rendering of node failure semantics: NodeErrorKind + node
// name become a human-readable message in the CURRENT APPLICATION language
// (SK/EN selection — never OS CurrentCulture). Infrastructure stays
// language-neutral: NodeErrorMapper keeps classifying into Kind + raw Detail,
// and callers use this formatter (with the authoritative node name they
// already hold) instead of the stored fixed-Slovak Message when building
// user-visible warnings. Raw provider Detail is appended by callers unchanged.
public static class NodeErrorText
{
    public static string For(NodeErrorKind kind, string node)
    {
        var loc = LocalizationService.Instance;
        var key = kind switch
        {
            NodeErrorKind.AccessDenied => "NodeErr_AccessDenied",
            NodeErrorKind.Timeout => "NodeErr_Timeout",
            NodeErrorKind.HostUnreachable => "NodeErr_HostUnreachable",
            NodeErrorKind.HyperVUnavailable => "NodeErr_HyperVUnavailable",
            NodeErrorKind.WmiUnavailable => "NodeErr_WmiUnavailable",
            NodeErrorKind.ClusterUnavailable => "NodeErr_ClusterUnavailable",
            _ => "NodeErr_Unknown",
        };
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, loc[key], node);
    }
}
