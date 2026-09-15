using Pi1.HyperVToolkit.Core.Localization;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.State;
using Pi1.HyperVToolkit.Infrastructure.Common;

namespace Pi1.HyperVToolkit.Tests;

// Node failure messages follow the application SK/EN selection (never OS
// culture). Classification itself is untouched (see NodeErrorMapperTests);
// these prove every rendered kind in both languages plus live switching.
[Collection("LanguageSerial")]
public sealed class NodeErrorTextTests
{
    public static TheoryData<NodeErrorKind, string, string> KindTexts() => new()
    {
        { NodeErrorKind.AccessDenied, "Prístup odmietnutý (N1).", "Access denied (N1)." },
        { NodeErrorKind.Timeout, "Časový limit vypršal (N1).", "The operation timed out (N1)." },
        { NodeErrorKind.HostUnreachable, "Hostiteľ je nedostupný (N1).", "Host is unreachable (N1)." },
        { NodeErrorKind.HyperVUnavailable, "Hyper-V nie je na uzle dostupný (N1).", "Hyper-V is not available on the node (N1)." },
        { NodeErrorKind.WmiUnavailable, "CIM/WMI dotaz zlyhal (N1).", "CIM/WMI query failed (N1)." },
        { NodeErrorKind.ClusterUnavailable, "Klaster nie je dostupný (N1).", "Cluster is not available (N1)." },
        { NodeErrorKind.Unknown, "Dotaz zlyhal (N1).", "Query failed (N1)." },
        { NodeErrorKind.Cancelled, "Dotaz zlyhal (N1).", "Query failed (N1)." },
    };

    [Theory]
    [MemberData(nameof(KindTexts))]
    public void EveryKind_RendersInBothLanguages(NodeErrorKind kind, string expectedSk, string expectedEn)
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            LocalizationService.Instance.SetLanguage(AppLanguage.Slovak);
            Assert.Equal(expectedSk, NodeErrorText.For(kind, "N1"));

            LocalizationService.Instance.SetLanguage(AppLanguage.English);
            Assert.Equal(expectedEn, NodeErrorText.For(kind, "N1"));
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public void LanguageSwitch_ChangesRenderingWithoutRestart()
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            LocalizationService.Instance.SetLanguage(AppLanguage.Slovak);
            Assert.Equal("Prístup odmietnutý (N1).", NodeErrorText.For(NodeErrorKind.AccessDenied, "N1"));
            LocalizationService.Instance.SetLanguage(AppLanguage.English);
            Assert.Equal("Access denied (N1).", NodeErrorText.For(NodeErrorKind.AccessDenied, "N1"));
            LocalizationService.Instance.SetLanguage(AppLanguage.Slovak);
            Assert.Equal("Prístup odmietnutý (N1).", NodeErrorText.For(NodeErrorKind.AccessDenied, "N1"));
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    // End-to-end pipeline: classify a real provider exception, then render
    // the kind in English. Classification inputs mirror NodeErrorMapperTests.
    public static TheoryData<Exception, bool, NodeErrorKind, string> PipelineCases() => new()
    {
        { new UnauthorizedAccessException("Access is denied."), false, NodeErrorKind.AccessDenied, "Access denied (N1)." },
        { new TimeoutException("WSMan timeout."), false, NodeErrorKind.Timeout, "The operation timed out (N1)." },
        { new InvalidOperationException("The RPC server is unavailable. (0x800706BA)"), false, NodeErrorKind.HostUnreachable, "Host is unreachable (N1)." },
        { new InvalidOperationException("Could not be resolved."), false, NodeErrorKind.HostUnreachable, "Host is unreachable (N1)." },
        { new InvalidOperationException("Invalid namespace (0x8004100E)"), true, NodeErrorKind.HyperVUnavailable, "Hyper-V is not available on the node (N1)." },
        { new InvalidOperationException("WMI generic failure 0x80041001"), false, NodeErrorKind.WmiUnavailable, "CIM/WMI query failed (N1)." },
        { new InvalidOperationException("Something odd."), false, NodeErrorKind.Unknown, "Query failed (N1)." },
    };

    [Theory]
    [MemberData(nameof(PipelineCases))]
    public void ClassifyThenRender_English(Exception exception, bool hyperVNamespace, NodeErrorKind kind, string expectedEn)
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            LocalizationService.Instance.SetLanguage(AppLanguage.English);
            var error = NodeErrorMapper.Map("N1", exception, hyperVNamespace);
            Assert.Equal(kind, error.Kind);
            Assert.Equal(expectedEn, NodeErrorText.For(error.Kind, "N1"));
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public void SlovakRendering_MatchesLegacyMapperText()
    {
        // Slovak output is byte-identical to the historical fixed strings, so
        // existing SK behavior cannot regress silently.
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            LocalizationService.Instance.SetLanguage(AppLanguage.Slovak);
            Assert.Equal("Prístup odmietnutý (N1).", NodeErrorText.For(NodeErrorMapper.Map("N1", new UnauthorizedAccessException()).Kind, "N1"));
            Assert.Equal("Časový limit vypršal (N1).", NodeErrorText.For(NodeErrorMapper.Map("N1", new TimeoutException()).Kind, "N1"));
            Assert.Equal("Hostiteľ je nedostupný (N1).", NodeErrorText.For(NodeErrorMapper.Map("N1", new InvalidOperationException("RPC server is unavailable")).Kind, "N1"));
            Assert.Equal("CIM/WMI dotaz zlyhal (N1).", NodeErrorText.For(NodeErrorMapper.Map("N1", new InvalidOperationException("WMI 0x80041001")).Kind, "N1"));
            Assert.Equal("Dotaz zlyhal (N1).", NodeErrorText.For(NodeErrorMapper.Map("N1", new InvalidOperationException("odd")).Kind, "N1"));
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }
}
