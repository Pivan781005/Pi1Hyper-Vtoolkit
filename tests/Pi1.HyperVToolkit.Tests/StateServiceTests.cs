using Pi1.HyperVToolkit.Core.State;
using Pi1.HyperVToolkit.Infrastructure.Paths;

namespace Pi1.HyperVToolkit.Tests;

public sealed class ReportServiceTests
{
    private sealed record SampleRow(string Name, int Value);

    [Fact]
    public void Initial_IsEmpty()
    {
        var service = new ReportService();
        Assert.Equal(string.Empty, service.CurrentName);
        Assert.Equal(0, service.Count);
        Assert.Null(service.RowType);
        Assert.Null(service.CurrentRows);
        Assert.Empty(service.GetRows<SampleRow>());
    }

    [Fact]
    public void SetCurrent_ExposesRowsNameAndCount()
    {
        var service = new ReportService();
        var rows = new List<SampleRow> { new("A", 1), new("B", 2) };
        var raised = 0;
        service.CurrentChanged += (_, _) => raised++;

        service.SetCurrent<SampleRow>(rows, "Sample");

        Assert.Equal("Sample", service.CurrentName);
        Assert.Equal(2, service.Count);
        Assert.Equal(typeof(SampleRow), service.RowType);
        Assert.Equal(rows, service.GetRows<SampleRow>());
        Assert.Equal(1, raised);
    }

    [Fact]
    public void GetRows_WrongType_ReturnsEmpty()
    {
        var service = new ReportService();
        service.SetCurrent([new SampleRow("A", 1)], "Sample");
        Assert.Empty(service.GetRows<string>());
    }

    [Fact]
    public void Clear_Resets()
    {
        var service = new ReportService();
        service.SetCurrent([new SampleRow("A", 1)], "Sample");
        service.Clear();
        Assert.Equal(string.Empty, service.CurrentName);
        Assert.Equal(0, service.Count);
        Assert.Null(service.CurrentRows);
    }
}

public sealed class AppPathsTests : IDisposable
{
    private readonly string _tempDir;

    public AppPathsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Pi1Tests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort test cleanup.
        }
    }

    [Fact]
    public void Layout_UsesExpectedSegments()
    {
        var paths = new AppPaths(_tempDir);
        Assert.Equal(_tempDir, paths.AppDataDirectory);
        Assert.Equal(Path.Combine(_tempDir, "Logs"), paths.LogsDirectory);
        Assert.Equal(Path.Combine(_tempDir, "settings.json"), paths.SettingsFilePath);
    }

    [Fact]
    public void DefaultLayout_IsUnderLocalAppData()
    {
        var paths = new AppPaths();
        var expectedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pi1", "HyperVToolkit");
        Assert.Equal(expectedRoot, paths.AppDataDirectory);
    }

    [Fact]
    public void EnsureCreated_CreatesDirectories()
    {
        var paths = new AppPaths(_tempDir);
        paths.EnsureCreated();
        Assert.True(Directory.Exists(paths.AppDataDirectory));
        Assert.True(Directory.Exists(paths.LogsDirectory));
    }
}
