using Pi1.HyperVToolkit.Core.Export;
using Pi1.HyperVToolkit.Infrastructure.Export;

namespace Pi1.HyperVToolkit.Tests;

// Non-interactive smoke: the real writer persists UTF-8 WITH BOM so Excel /
// Notepad open diacritics correctly (Export-Csv -Encoding UTF8 parity).
public sealed class DiskFileWriterTests
{
    [Fact]
    public async Task WritesUtf8Bom_WithDiacriticsRoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"Pi1Export_{Guid.NewGuid():N}.csv");
        try
        {
            var writer = new DiskFileWriter();
            await writer.WriteAllTextAsync(path, "\"Piešťany\"\r\n", ExportEncodings.Utf8Bom);

            var bytes = await File.ReadAllBytesAsync(path);
            Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
            Assert.Equal("\"Piešťany\"\r\n", await File.ReadAllTextAsync(path));
        }
        finally
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // Best-effort temp cleanup only.
            }
        }
    }
}
