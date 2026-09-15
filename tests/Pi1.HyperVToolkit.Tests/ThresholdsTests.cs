using Pi1.HyperVToolkit.Core.Utilities;

namespace Pi1.HyperVToolkit.Tests;

/// <summary>
/// Guards the frozen Phase 1 values. If any of these fail, parity with
/// PowerShell v0.9.4 was broken — do not "fix" the test, fix the code.
/// </summary>
public sealed class ThresholdsTests
{
    [Fact]
    public void TableColourRules_AreFrozen()
    {
        Assert.Equal(85.0, Thresholds.RamUsedRedPercent);
        Assert.Equal(70.0, Thresholds.RamUsedYellowPercent);
        Assert.Equal(10.0, Thresholds.FreePercentRed);
        Assert.Equal(20.0, Thresholds.FreePercentYellow);
        Assert.Equal(8.0, Thresholds.WasteYellowGb);
    }

    [Fact]
    public void AdvisorRules_AreFrozen()
    {
        Assert.Equal(8.0, Thresholds.WasteInfoGb);
        Assert.Equal(16.0, Thresholds.WasteLargeInfoGb);
        Assert.Equal("SQL|VEEAM|EXCHANGE|FORTI|FAZ", Thresholds.SensitiveWorkloadPattern);
    }

    [Fact]
    public void CsvCutoffs_AreDistinctByDesign()
    {
        // Dashboard + Health Score use 15 %, the Diagnostics low-free view uses 20 %.
        Assert.Equal(15.0, Thresholds.CsvWarningPercent);
        Assert.Equal(20.0, Thresholds.CsvLowFreeViewPercent);
        Assert.NotEqual(Thresholds.CsvWarningPercent, Thresholds.CsvLowFreeViewPercent);
    }

    [Fact]
    public void HealthScoreDeductions_AreFrozen()
    {
        Assert.Equal(100, Thresholds.ScoreStart);
        Assert.Equal(30, Thresholds.ScoreNodesDownDeduction);
        Assert.Equal(20, Thresholds.ScoreBadResourcesDeduction);
        Assert.Equal(15, Thresholds.ScoreLowCsvDeduction);
        Assert.Equal(10, Thresholds.ScoreStorageJobsDeduction);
        Assert.Equal(10, Thresholds.ScoreNoQuorumDeduction);
        Assert.Equal(5, Thresholds.ScoreMemoryPressureDeduction);
        Assert.Equal(90, Thresholds.ScoreExcellentBand);
        Assert.Equal(70, Thresholds.ScoreGoodBand);
    }

    [Fact]
    public void FailoverAndPlacementBands_AreFrozen()
    {
        Assert.Equal(95.0, Thresholds.FailoverDemandCriticalPercent);
        Assert.Equal(85.0, Thresholds.FailoverDemandWarningPercent);
        Assert.Equal(95.0, Thresholds.FailoverAssignedInfoPercent);
        Assert.Equal(85.0, Thresholds.PlacementDemandWarningPercent);
        Assert.Equal(90.0, Thresholds.PlacementAssignedInfoPercent);
        Assert.Equal("High|3000", Thresholds.HighPriorityPattern);
        Assert.Equal(30, Thresholds.ClusterEventsMaxCount);
    }

    [Fact]
    public void RoundingMatchesPowerShellMathRound()
    {
        // [math]::Round(x,1) == Math.Round(x,1) == MidpointRounding.ToEven.
        Assert.Equal(2.2, Math.Round(2.25, 1));
        Assert.Equal(2.4, Math.Round(2.35, 1));
    }
}
