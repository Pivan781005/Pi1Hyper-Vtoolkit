namespace Pi1.HyperVToolkit.Infrastructure.Cim;

// Pure retry policy for native CIM reads. Evidence: parallel dashboard
// fan-outs share one cached CimSession per node string, and a session fault
// EVICTS (disposes) that session while sibling queries are still in flight
// on it. Those siblings then fail deterministically with
// ObjectDisposedException — a cascade victim, not a real provider failure.
// Retrying such victims once against a fresh session converts a false
// startup banner into the healthy-empty result the retry then observes.
// Persistent faults (second failure, cancellation, anything else) always
// propagate so real provider errors stay visible.
public static class CimRetryPolicy
{
    public static bool ShouldRetry(Exception exception, int attempt)
    {
        if (attempt >= 2)
        {
            return false;
        }

        return exception is Microsoft.Management.Infrastructure.CimException
            or ObjectDisposedException;
    }
}
