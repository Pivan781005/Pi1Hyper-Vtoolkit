using System.Text;
using Pi1.HyperVToolkit.Core.Services;

namespace Pi1.HyperVToolkit.Infrastructure.Export;

// Straight local file write. Callers pass an explicit encoding
// (ExportEncodings.Utf8Bom) so output never depends on machine defaults.
public sealed class DiskFileWriter : IExportFileWriter
{
    public Task WriteAllTextAsync(string path, string content, Encoding encoding, CancellationToken cancellationToken = default) =>
        File.WriteAllTextAsync(path, content, encoding, cancellationToken);
}
