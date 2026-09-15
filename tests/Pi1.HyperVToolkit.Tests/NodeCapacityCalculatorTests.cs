using Pi1.HyperVToolkit.Core.Calculations;
using Pi1.HyperVToolkit.Core.Models;

namespace Pi1.HyperVToolkit.Tests;

public sealed class NodeCapacityCalculatorTests
{
    private static VirtualMachineRow Vm(string node, string name, string state, int cpu, double assigned, double demand) =>
        new(node, name, state, cpu, assigned, demand, Math.Round(assigned - demand, 1),
            string.Empty, string.Empty, string.Empty, null, false, 0, 0, 0);

    private static NodeHardwareRow Hw(double ram) =>
        new("NODE1", string.Empty, string.Empty, string.Empty, string.Empty, null,
            string.Empty, 1, 4, 8, ram, ram / 2, ram / 2, 50.0);

    [Fact]
    public void Compute_AggregatesRunningOnly()
    {
        var vms = new[]
        {
            Vm("NODE1", "A", "Running", 2, 8.0, 6.0),
            Vm("NODE1", "B", "Running", 4, 16.0, 20.0),
            Vm("NODE1", "C", "Off", 2, 4.0, 0.0),
            Vm("NODE1", "D", "Paused", 2, 4.0, 4.0),
        };
        var row = NodeCapacityCalculator.Compute("NODE1", vms, Hw(64));

        Assert.Equal(2, row.RunningVM);
        Assert.Equal(1, row.OffVM); // Paused counts in neither bucket (PS parity)
        Assert.Equal(6, row.VCpu);
        Assert.Equal(24.0, row.AssignedGB);
        Assert.Equal(26.0, row.DemandGB);
        Assert.Equal(-2.0, row.WasteGB);
        Assert.Equal(37.5, row.AssignedPct);
        Assert.Equal(40.6, row.DemandPct);
    }

    [Fact]
    public void Compute_NoRunning_NullVCpuZeroMemory()
    {
        var row = NodeCapacityCalculator.Compute("NODE1", [Vm("NODE1", "A", "Off", 2, 4.0, 0.0)], Hw(64));
        Assert.Equal(0, row.RunningVM);
        Assert.Null(row.VCpu);
        Assert.Equal(0, row.AssignedGB);
        Assert.Equal(0, row.DemandGB);
    }

    [Fact]
    public void Compute_UnknownRam_NullPercentages()
    {
        var row = NodeCapacityCalculator.Compute("NODE1", [Vm("NODE1", "A", "Running", 2, 8.0, 6.0)], hardware: null);
        Assert.Null(row.AssignedPct);
        Assert.Null(row.DemandPct);
        Assert.Equal(8.0, row.AssignedGB);
    }

    [Fact]
    public void Compute_IgnoresOtherNodes()
    {
        var vms = new[] { Vm("NODE1", "A", "Running", 2, 8.0, 6.0), Vm("NODE2", "B", "Running", 8, 32.0, 30.0) };
        var row = NodeCapacityCalculator.Compute("NODE1", vms, Hw(64));
        Assert.Equal(1, row.RunningVM);
        Assert.Equal(8.0, row.AssignedGB);
    }
}
