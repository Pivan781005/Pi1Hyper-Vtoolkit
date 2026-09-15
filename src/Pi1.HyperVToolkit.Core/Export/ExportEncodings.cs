using System.Text;

namespace Pi1.HyperVToolkit.Core.Export;

// CSV is UTF-8 WITH BOM for reliable Windows Excel/Notepad compatibility
// (matches Export-Csv -Encoding UTF8 on Windows PowerShell 5.1, which emits
// a BOM). HTML/JSON writers use the same explicit encoding.
public static class ExportEncodings
{
    public static Encoding Utf8Bom { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
}
