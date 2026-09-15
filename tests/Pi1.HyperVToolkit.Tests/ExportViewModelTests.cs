using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Pi1.HyperVToolkit.Core.Export;
using Pi1.HyperVToolkit.Core.Localization;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Services;
using Pi1.HyperVToolkit.Core.State;
using Pi1.HyperVToolkit.ViewModels;

namespace Pi1.HyperVToolkit.Tests;

// Current-result integrity, snapshot stability, empty/unsupported handling,
// Full VM report (fresh, partial, no-targets), dialog cancel, failure mapping
// and frozen-timestamp filenames. Language-sensitive: serialized with other
// language-dependent classes.
[Collection("LanguageSerial")]
public sealed class ExportViewModelTests
{
    private static readonly DateTimeOffset Frozen =
        new(2026, 9, 15, 12, 34, 56, TimeSpan.Zero);

    private sealed class FrozenClock : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FrozenClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class FakeTargets : ITargetNodeResolver
    {
        public string[] Nodes = ["N1"];
        public string[] Warnings = [];
        public Task<TargetNodeSet> ResolveAsync(ScopeState scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TargetNodeSet(Nodes.ToList(), Warnings.ToList()));
    }

    private sealed class FakeHyperV : IHyperVService
    {
        public List<VirtualMachineRow> VmRows =
        [
            new("N1", "APP1", "Running", 2, 4.0, 3.0, 1.0, "10.0.0.1", "AA", "SW", null, true, 4.0, 1.0, 8.0),
            new("N1", "APP2", "Off", 1, 2.0, 2.0, 0.0, string.Empty, string.Empty, string.Empty, null, false, 2.0, 1.0, 2.0),
        ];

        public string? FailNodeName;
        public int Calls;

        public Task<IReadOnlyList<NodeResult<IReadOnlyList<VirtualMachineRow>>>> GetVirtualMachinesAsync(
            IReadOnlyList<string> nodes, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            var results = new List<NodeResult<IReadOnlyList<VirtualMachineRow>>>();
            foreach (var node in nodes)
            {
                if (FailNodeName is not null && string.Equals(node, FailNodeName, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(NodeResult<IReadOnlyList<VirtualMachineRow>>.Fail(
                        node, new NodeError(NodeErrorKind.HostUnreachable, $"Node {node} unreachable", node)));
                }
                else
                {
                    results.Add(NodeResult<IReadOnlyList<VirtualMachineRow>>.Ok(node, VmRows));
                }
            }

            return Task.FromResult<IReadOnlyList<NodeResult<IReadOnlyList<VirtualMachineRow>>>>(results);
        }

        public Task<IReadOnlyList<NodeResult<IReadOnlyList<VmNetworkRow>>>> GetVmNetworkAdaptersAsync(
            IReadOnlyList<string> nodes, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NodeResult<IReadOnlyList<VmNetworkRow>>>>([]);
        public Task<IReadOnlyList<NodeResult<IReadOnlyList<CheckpointRow>>>> GetCheckpointsAsync(
            IReadOnlyList<string> nodes, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NodeResult<IReadOnlyList<CheckpointRow>>>>([]);
        public Task<IReadOnlyList<NodeResult<HostSettingsRow>>> GetHostSettingsAsync(
            IReadOnlyList<string> nodes, IReadOnlyDictionary<string, NodeHardwareRow?> hardwareByNode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NodeResult<HostSettingsRow>>>([]);
        public Task<IReadOnlyList<NodeResult<IReadOnlyList<VmSwitchRow>>>> GetVirtualSwitchesAsync(
            IReadOnlyList<string> nodes, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NodeResult<IReadOnlyList<VmSwitchRow>>>>([]);
        public Task<IReadOnlyList<NodeResult<IReadOnlyList<VmVlanRow>>>> GetVmAdapterVlansAsync(
            IReadOnlyList<string> nodes, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NodeResult<IReadOnlyList<VmVlanRow>>>>([]);
        public Task<IReadOnlyList<NodeResult<IReadOnlyList<VmStorageRow>>>> GetVmStorageAsync(
            IReadOnlyList<string> nodes, IReadOnlyList<CsvRow> csvRows,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NodeResult<IReadOnlyList<VmStorageRow>>>>([]);
    }

    private sealed class FakeDialog : IExportDialogService
    {
        public string? PathToReturn = "C:\\Exports\\out.csv";
        public string? SeenDefaultFileName;
        public string? SeenFilter;
        public string? SeenExtension;
        public int Calls;
        public Action? OnShow;

        public string? ShowSaveDialog(string defaultFileName, string filter, string defaultExtension)
        {
            Calls++;
            SeenDefaultFileName = defaultFileName;
            SeenFilter = filter;
            SeenExtension = defaultExtension;
            OnShow?.Invoke();
            return PathToReturn;
        }
    }

    private sealed class FakeWriter : IExportFileWriter
    {
        public List<(string Path, string Content, Encoding Encoding)> Writes = [];
        public Exception? ThrowOnWrite;

        public Task WriteAllTextAsync(string path, string content, Encoding encoding, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowOnWrite is not null)
            {
                throw ThrowOnWrite;
            }

            Writes.Add((path, content, encoding));
            return Task.CompletedTask;
        }
    }

    private static ExportViewModel Create(
        ReportService report,
        ScopeService scope,
        FakeTargets targets,
        FakeHyperV hyperV,
        FakeDialog dialog,
        FakeWriter writer,
        ISessionCache? cache = null)
    {
        LocalizationService.Instance.SetLanguage(AppLanguage.Slovak);
        return new ExportViewModel(
            report, scope, targets, hyperV, cache ?? new SessionCache(),
            dialog, writer, new FrozenClock(Frozen),
            NullLogger<ExportViewModel>.Instance);
    }

    [Fact]
    public async Task NavigateToExport_DoesNotDestroyCurrentReport()
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            var report = new ReportService();
            var rows = new List<DashboardRow> { new("Cluster", "OK", "CLU"), new("Scope", "Info", "S") };
            report.SetCurrent(rows, "Dashboard");
            var vm = Create(report, new ScopeService("N1"), new FakeTargets(), new FakeHyperV(), new FakeDialog(), new FakeWriter());

            await vm.RefreshAsync(CancellationToken.None);

            Assert.Equal("Dashboard", report.CurrentName);
            Assert.Equal(2, report.Count);
            Assert.True(vm.HasReport);
            Assert.Equal("Dashboard", vm.CurrentReportName);
            Assert.Equal(2, vm.CurrentReportRows);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public async Task LatestPublish_Wins()
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            var report = new ReportService();
            report.SetCurrent(new List<DashboardRow> { new("A", "OK", "1") }, "Dashboard");
            var vm = Create(report, new ScopeService("N1"), new FakeTargets(), new FakeHyperV(), new FakeDialog(), new FakeWriter());
            report.SetCurrent(
                new List<AdvisorRow> { new("Warning", "N1", "SQL01", 4.0, 5.0, -1.0, AdvisorRule.Pressure, true) },
                "Advisor");

            await vm.RefreshAsync(CancellationToken.None);

            Assert.Equal("Advisor", vm.CurrentReportName);
            Assert.Equal(1, vm.CurrentReportRows);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public async Task NoReport_NoFile_NoDialog()
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            var dialog = new FakeDialog();
            var writer = new FakeWriter();
            var vm = Create(new ReportService(), new ScopeService("N1"), new FakeTargets(), new FakeHyperV(), dialog, writer);

            await vm.ExportCurrentCsvCommand.ExecuteAsync(null);

            Assert.Equal(0, dialog.Calls);
            Assert.Empty(writer.Writes);
            Assert.Equal("Nie je k dispozícii žiadny výsledok na export.", vm.StatusText);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public async Task ZeroRowReport_NoFile()
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            var report = new ReportService();
            report.SetCurrent(new List<DashboardRow>(), "Dashboard");
            var dialog = new FakeDialog();
            var writer = new FakeWriter();
            var vm = Create(report, new ScopeService("N1"), new FakeTargets(), new FakeHyperV(), dialog, writer);

            await vm.ExportCurrentJsonCommand.ExecuteAsync(null);

            Assert.Equal(0, dialog.Calls);
            Assert.Empty(writer.Writes);
            Assert.Equal("Nie je k dispozícii žiadny výsledok na export.", vm.StatusText);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public async Task UnsupportedSchema_NoFile_NoLeak()
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            var report = new ReportService();
            report.SetCurrent(new List<string> { "secret-internal-payload" }, "MysteryReport");
            var dialog = new FakeDialog();
            var writer = new FakeWriter();
            var vm = Create(report, new ScopeService("N1"), new FakeTargets(), new FakeHyperV(), dialog, writer);

            await vm.ExportCurrentCsvCommand.ExecuteAsync(null);

            Assert.Equal(0, dialog.Calls);
            Assert.Empty(writer.Writes);
            Assert.Equal("Tento výpis sa nedá exportovať (neznáma schéma).", vm.StatusText);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public async Task ExportedSnapshot_IsStableAgainstLaterMutation()
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            var report = new ReportService();
            var rows = new List<AdvisorRow>
            {
                new("Warning", "N1", "SQL01", 4.0, 5.0, -1.0, AdvisorRule.Pressure, true),
                new("Info", "N1", "APP1", 20.0, 5.0, 15.0, AdvisorRule.Reserve, false),
            };
            report.SetCurrent(rows, "Advisor");
            var dialog = new FakeDialog { PathToReturn = "C:\\Exports\\a.csv" };
            dialog.OnShow = report.Clear; // navigation away mid-export cannot mutate the snapshot
            var writer = new FakeWriter();
            var vm = Create(report, new ScopeService("N1"), new FakeTargets(), new FakeHyperV(), dialog, writer);

            await vm.ExportCurrentCsvCommand.ExecuteAsync(null);

            var (_, content, _) = Assert.Single(writer.Writes);
            Assert.Contains("SQL01", content, StringComparison.Ordinal);
            Assert.Contains("APP1", content, StringComparison.Ordinal);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public async Task CurrentCsv_Success_FrozenNameFilterEncoding()
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            var report = new ReportService();
            report.SetCurrent(
                new List<AdvisorRow> { new("Warning", "N1", "SQL01", 4.0, 5.0, -1.0, AdvisorRule.Pressure, true) },
                "Advisor");
            var dialog = new FakeDialog { PathToReturn = "C:\\Exports\\Advisor_20260915_123456.csv" };
            var writer = new FakeWriter();
            var vm = Create(report, new ScopeService("N1"), new FakeTargets(), new FakeHyperV(), dialog, writer);

            await vm.ExportCurrentCsvCommand.ExecuteAsync(null);

            Assert.Equal(1, dialog.Calls);
            Assert.Equal("Advisor_20260915_123456.csv", dialog.SeenDefaultFileName);
            Assert.Equal("CSV (*.csv)|*.csv", dialog.SeenFilter);
            Assert.Equal("csv", dialog.SeenExtension);
            var (path, content, encoding) = Assert.Single(writer.Writes);
            Assert.Equal("C:\\Exports\\Advisor_20260915_123456.csv", path);
            Assert.Equal("C:\\Exports\\Advisor_20260915_123456.csv", vm.LastExportedPath);
            Assert.Equal(3, encoding.GetPreamble().Length); // UTF-8 BOM
            Assert.StartsWith("\"Severity\",\"HostNode\",\"VM\"", content, StringComparison.Ordinal);
            Assert.StartsWith("Export hotový:", vm.StatusText, StringComparison.Ordinal);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public async Task DialogCancel_NoFile_CancelledStatus()
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            var report = new ReportService();
            report.SetCurrent(new List<DashboardRow> { new("A", "OK", "1") }, "Dashboard");
            var dialog = new FakeDialog { PathToReturn = null };
            var writer = new FakeWriter();
            var vm = Create(report, new ScopeService("N1"), new FakeTargets(), new FakeHyperV(), dialog, writer);

            await vm.ExportCurrentHtmlCommand.ExecuteAsync(null);

            Assert.Empty(writer.Writes);
            Assert.Equal("Export zrušený.", vm.StatusText);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public async Task WriteFailure_LocalizedStatus_NoThrow()
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            var report = new ReportService();
            report.SetCurrent(new List<DashboardRow> { new("A", "OK", "1") }, "Dashboard");
            var writer = new FakeWriter { ThrowOnWrite = new IOException("disk full") };
            var vm = Create(report, new ScopeService("N1"), new FakeTargets(), new FakeHyperV(), new FakeDialog(), writer);

            await vm.ExportCurrentJsonCommand.ExecuteAsync(null);

            Assert.StartsWith("Export zlyhal:", vm.StatusText, StringComparison.Ordinal);
            Assert.Contains("disk full", vm.StatusText, StringComparison.Ordinal);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public async Task FullVmCsv_FreshRows_SemanticColumns()
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            var hyperV = new FakeHyperV();
            var dialog = new FakeDialog { PathToReturn = "C:\\Exports\\FullVMReport_20260915_123456.csv" };
            var writer = new FakeWriter();
            var vm = Create(new ReportService(), new ScopeService("N1"), new FakeTargets(), hyperV, dialog, writer);

            await vm.ExportFullVmCsvCommand.ExecuteAsync(null);

            Assert.Equal(1, hyperV.Calls);
            Assert.Equal("FullVMReport_20260915_123456.csv", dialog.SeenDefaultFileName);
            var (_, content, _) = Assert.Single(writer.Writes);
            Assert.StartsWith("\"HostNode\",\"VM\",\"State\",\"CPU\"", content, StringComparison.Ordinal);
            Assert.Contains("\"APP1\"", content, StringComparison.Ordinal);
            Assert.Contains("\"APP2\"", content, StringComparison.Ordinal);
            Assert.DoesNotContain("AssignedBytes", content, StringComparison.Ordinal);
            Assert.True(content.IndexOf("\"APP1\"", StringComparison.Ordinal) < content.IndexOf("\"APP2\"", StringComparison.Ordinal));
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public async Task FullVm_PartialFailure_KeepsGoodRows_Warns()
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            var hyperV = new FakeHyperV
            {
                FailNodeName = "N2",
            };
            var targets = new FakeTargets { Nodes = ["N1", "N2"] };
            var dialog = new FakeDialog { PathToReturn = "C:\\Exports\\f.csv" };
            var writer = new FakeWriter();
            var vm = Create(new ReportService(), new ScopeService("N1"), targets, hyperV, dialog, writer);

            await vm.ExportFullVmCsvCommand.ExecuteAsync(null);

            var (_, content, _) = Assert.Single(writer.Writes);
            Assert.Contains("\"APP1\"", content, StringComparison.Ordinal);
            Assert.True(vm.HasWarnings);
            Assert.Contains("Čiastočné údaje:", vm.StatusText, StringComparison.Ordinal);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public async Task FullVm_NoTargets_NoFile()
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            var targets = new FakeTargets { Nodes = [], Warnings = ["nope"] };
            var dialog = new FakeDialog();
            var writer = new FakeWriter();
            var vm = Create(new ReportService(), new ScopeService("N1"), targets, new FakeHyperV(), dialog, writer);

            await vm.ExportFullVmJsonCommand.ExecuteAsync(null);

            Assert.Equal(0, dialog.Calls);
            Assert.Empty(writer.Writes);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public async Task FriendlyDisplayName_Localized_CanonicalKept()
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            var report = new ReportService();
            report.SetCurrent(
                new List<PhysicalDiskRow> { new("D1", "SSD", "NVMe", 100.0, "Healthy", "OK", true, "Auto", "SN1") },
                "PhysicalDisks");
            var vm = Create(report, new ScopeService("N1"), new FakeTargets(), new FakeHyperV(), new FakeDialog(), new FakeWriter());

            await vm.RefreshAsync(CancellationToken.None);

            Assert.Equal("PhysicalDisks", vm.CurrentReportName);
            Assert.Equal("Fyzické disky", vm.CurrentReportDisplayName);

            LocalizationService.Instance.SetLanguage(AppLanguage.English);
            await vm.RefreshAsync(CancellationToken.None);
            Assert.Equal("Physical Disks", vm.CurrentReportDisplayName);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    [Theory]
    [InlineData(ScopeMode.Local, "N1", "Lokálny uzol (N1)", "Local node (N1)")]
    [InlineData(ScopeMode.Cluster, "N1", "Všetky uzly klastra", "All cluster nodes")]
    [InlineData(ScopeMode.Node, "N2", "Vybraný uzol (N2)", "Selected node (N2)")]
    public async Task ScopeLabel_FollowsApplicationLanguage(
        ScopeMode mode, string node, string expectedSk, string expectedEn)
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            var scope = new ScopeService("N1");
            scope.SetScope(mode, node);
            var vm = Create(new ReportService(), scope, new FakeTargets(), new FakeHyperV(), new FakeDialog(), new FakeWriter());

            LocalizationService.Instance.SetLanguage(AppLanguage.Slovak);
            await vm.RefreshAsync(CancellationToken.None);
            Assert.Equal(expectedSk, vm.ScopeLabel);

            LocalizationService.Instance.SetLanguage(AppLanguage.English);
            await vm.RefreshAsync(CancellationToken.None);
            Assert.Equal(expectedEn, vm.ScopeLabel);

            LocalizationService.Instance.SetLanguage(AppLanguage.Slovak);
            await vm.RefreshAsync(CancellationToken.None);
            Assert.Equal(expectedSk, vm.ScopeLabel);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public async Task English_NoResultMessage()
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            LocalizationService.Instance.SetLanguage(AppLanguage.English);
            var vm = new ExportViewModel(
                new ReportService(), new ScopeService("N1"), new FakeTargets(), new FakeHyperV(), new SessionCache(),
                new FakeDialog(), new FakeWriter(), new FrozenClock(Frozen),
                NullLogger<ExportViewModel>.Instance);

            await vm.ExportCurrentCsvCommand.ExecuteAsync(null);

            Assert.Equal("There is no result available to export.", vm.StatusText);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }
}
