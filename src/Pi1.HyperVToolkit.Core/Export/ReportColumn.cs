namespace Pi1.HyperVToolkit.Core.Export;

// Logical value type of one report column. Controls JSON typing;
// CSV/HTML always render invariant display text.
public enum ReportColumnKind
{
    Text = 0,
    Number = 1,
    Boolean = 2,
}

// One ordered export column. Id is the stable canonical (PowerShell/JSON)
// field name; HeaderKey resolves the human-readable HTML header per UI
// language. GetValue returns the raw CLR value (string, double/int/bool or
// null) — never formatted text, never an internal implementation detail.
public sealed record ReportColumn(
    string Id,
    string HeaderKey,
    ReportColumnKind Kind,
    Func<object, object?> GetValue);
