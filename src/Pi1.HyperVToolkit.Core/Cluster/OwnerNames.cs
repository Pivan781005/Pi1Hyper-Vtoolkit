namespace Pi1.HyperVToolkit.Core.Cluster;

// Structured owner-node normalization. Port of the INTENT of
// Convert-PiClusterOwnerNodeListToNames WITHOUT its fragile debt:
//  - plain non-empty strings are kept;
//  - objects contribute values from the documented candidate properties
//    (Name, NodeName, OwnerNode, ClusterNode, Node, ClusterObject.Name);
//  - unknown shapes are SKIPPED (never ToString()'d — the reference's
//    type-name leak, e.g. "ClusterOwnerNodeList", cannot occur);
//  - results are unique case-insensitively in first-occurrence order.
// Only format as "NODE1, NODE2" at the display boundary.
public static class OwnerNames
{
    private static readonly string[] CandidateProperties =
        ["Name", "NodeName", "OwnerNode", "ClusterNode", "Node"];

    public static IReadOnlyList<string> Normalize(IEnumerable<object?> values)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var trimmed = name.Trim();
            if (IsTypeNameLeak(trimmed))
            {
                return;
            }

            if (seen.Add(trimmed))
            {
                names.Add(trimmed);
            }
        }

        foreach (var item in values)
        {
            switch (item)
            {
                case null:
                    continue;
                case string text:
                    Add(text);
                    continue;
                case IReadOnlyDictionary<string, object?> bag:
                    // Production bags come from ICimQuerier (which already
                    // flattens CimInstance values), so dictionaries cover the
                    // object shapes without a Management.Infrastructure
                    // dependency in Core.
                    foreach (var candidate in CandidateProperties)
                    {
                        if (bag.TryGetValue(candidate, out var bagValue) && bagValue is string bagText)
                        {
                            Add(bagText);
                        }
                    }

                    if (bag.TryGetValue("ClusterObject", out var nestedObject) &&
                        nestedObject is IReadOnlyDictionary<string, object?> nestedBag &&
                        nestedBag.TryGetValue("Name", out var nestedBagName) &&
                        nestedBagName is string nestedBagText)
                    {
                        Add(nestedBagText);
                    }

                    continue;
                default:
                    // Unknown shapes are skipped deliberately: emitting an
                    // arbitrary ToString() is exactly the reference debt.
                    continue;
            }
        }

        return names;
    }

    internal static bool IsTypeNameLeak(string text) =>
        text.Contains("ClusterOwnerNodeList", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("ManagementObject", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("CimInstance", StringComparison.OrdinalIgnoreCase);
}
