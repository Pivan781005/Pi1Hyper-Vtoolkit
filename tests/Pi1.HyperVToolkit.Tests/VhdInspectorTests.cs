using Microsoft.Extensions.Logging.Abstractions;
using Pi1.HyperVToolkit.Infrastructure.Cim;
using Pi1.HyperVToolkit.Infrastructure.HyperV;

namespace Pi1.HyperVToolkit.Tests;

public sealed class VhdInspectorTests
{
    private const string RealCimXml = """
        <INSTANCE CLASSNAME="Msvm_VirtualHardDiskSettingData">
          <PROPERTY NAME="Format" TYPE="uint16"><VALUE>2</VALUE></PROPERTY>
          <PROPERTY NAME="MaxInternalSize" TYPE="uint64"><VALUE>2147483648</VALUE></PROPERTY>
          <PROPERTY NAME="Path" TYPE="string"><VALUE>C:\VHD\dos.avhd</VALUE></PROPERTY>
          <PROPERTY NAME="Type" TYPE="uint16"><VALUE>4</VALUE></PROPERTY>
        </INSTANCE>
        """;

    private sealed class FakeQuerier : ICimQuerier
    {
        public uint ReturnValue { get; set; }
        public string SettingData { get; set; } = RealCimXml;

        public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
            string node, string @namespace, string wql, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>([]);

        public Task<IReadOnlyDictionary<string, object?>> InvokeSingletonMethodAsync(
            string node, string @namespace, string className, string methodName,
            IReadOnlyDictionary<string, object?> inParameters, TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, object?>>(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ReturnValue"] = ReturnValue,
                    ["SettingData"] = SettingData,
                });
    }

    [Fact]
    public async Task Inspect_CimXml_MapsTypeFormatSize_PlusRealFileSize()
    {
        // Tiny real file: FileSize must come back present (81920 B), and the
        // GB projection is 0.0 — never null.
        var path = Path.Combine(Path.GetTempPath(), $"pi1-vhd-{Guid.NewGuid():N}.avhd");
        var payload = new byte[81920];
        new Random(42).NextBytes(payload);
        await File.WriteAllBytesAsync(path, payload);
        try
        {
            var inspector = new VhdInspector(new FakeQuerier(), NullLogger<VhdInspector>.Instance);
            var metadata = await inspector.InspectAsync(".", path);
            Assert.NotNull(metadata);
            Assert.Equal("Differencing", metadata!.VhdType);
            Assert.Equal("VHD", metadata.VhdFormat);
            Assert.Equal(2147483648UL, metadata.SizeBytes);
            Assert.Equal((ulong)payload.Length, metadata.FileSizeBytes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Inspect_FailedMethod_ReturnsNull()
    {
        var inspector = new VhdInspector(
            new FakeQuerier { ReturnValue = 1 }, NullLogger<VhdInspector>.Instance);
        Assert.Null(await inspector.InspectAsync(".", @"C:\VHD\dos.avhd"));
    }

    [Fact]
    public async Task Inspect_EmptyPath_ReturnsNull()
    {
        var inspector = new VhdInspector(new FakeQuerier(), NullLogger<VhdInspector>.Instance);
        Assert.Null(await inspector.InspectAsync(".", string.Empty));
    }
}
