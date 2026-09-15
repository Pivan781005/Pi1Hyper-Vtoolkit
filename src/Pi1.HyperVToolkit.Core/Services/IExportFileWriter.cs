using System.Text;

namespace Pi1.HyperVToolkit.Core.Services;

// Local report-file output behind a testable seam. The ONLY new write
// operation in the application (user-selected report files); Hyper-V,
// cluster and storage stay strictly read-only.
public interface IExportFileWriter
{
    Task WriteAllTextAsync(string path, string content, Encoding encoding, CancellationToken cancellationToken = default);
}
