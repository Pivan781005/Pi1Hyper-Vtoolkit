using Microsoft.Management.Infrastructure;
using Pi1.HyperVToolkit.Infrastructure.Cim;

namespace Pi1.HyperVToolkit.Tests;

public sealed class CimRetryPolicyTests
{
    [Fact]
    public void CimException_RetriedOnce()
    {
        Assert.True(CimRetryPolicy.ShouldRetry(new CimException("transient provider fault"), attempt: 1));
        Assert.False(CimRetryPolicy.ShouldRetry(new CimException("persistent fault"), attempt: 2));
    }

    [Fact]
    public void EvictedSessionVictim_RetriedOnce()
    {
        // Sibling query in flight on an evicted shared session: deterministic
        // ObjectDisposedException, still worth one fresh-session retry.
        Assert.True(CimRetryPolicy.ShouldRetry(new ObjectDisposedException("CimSession"), attempt: 1));
        Assert.False(CimRetryPolicy.ShouldRetry(new ObjectDisposedException("CimSession"), attempt: 2));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Cancellation_NeverRetried(int attempt)
    {
        Assert.False(CimRetryPolicy.ShouldRetry(new OperationCanceledException(), attempt));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void OtherErrors_NeverRetried(int attempt)
    {
        Assert.False(CimRetryPolicy.ShouldRetry(new InvalidOperationException("bad query"), attempt));
        Assert.False(CimRetryPolicy.ShouldRetry(new UnauthorizedAccessException(), attempt));
    }
}
