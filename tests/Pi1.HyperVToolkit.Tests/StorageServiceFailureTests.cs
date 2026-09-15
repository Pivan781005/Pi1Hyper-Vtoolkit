using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Management.Infrastructure;
using Pi1.HyperVToolkit.Infrastructure.Cim;
using Pi1.HyperVToolkit.Infrastructure.Storage;

namespace Pi1.HyperVToolkit.Tests;

// Guards §17: the storage service NEVER converts a provider error into a
// healthy-empty result (no `catch { return []; }`). Recovery retries live one
// layer below, inside MmiCimQuerier; persistent faults must reach the caller
// so the UI can warn.
public sealed class StorageServiceFailureTests
{
    private sealed class ThrowingQuerier : ICimQuerier
    {
        public Exception? ToThrow { get; set; }

        public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
            string node, string @namespace, string wql, TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            ToThrow is not null
                ? Task.FromException<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(ToThrow)
                : Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>([]);

        public Task<IReadOnlyDictionary<string, object?>> InvokeSingletonMethodAsync(
            string node, string @namespace, string className, string methodName,
            IReadOnlyDictionary<string, object?> inParameters, TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static StorageService Create(out ThrowingQuerier querier)
    {
        querier = new ThrowingQuerier();
        return new StorageService(querier, NullLogger<StorageService>.Instance);
    }

    [Fact]
    public async Task ProviderError_Propagates_NotSwallowed()
    {
        var service = Create(out var querier);
        querier.ToThrow = new InvalidOperationException("provider unavailable");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetStorageJobsAsync());
        querier.ToThrow = null;
        var jobs = await service.GetStorageJobsAsync(); // healthy-empty still works
        Assert.Empty(jobs);
    }

    [Fact]
    public async Task CimFault_Propagates_ToCaller()
    {
        var service = Create(out var querier);
        querier.ToThrow = new CimException("transient CIM fault");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetStorageJobsAsync());
    }

    [Fact]
    public async Task EvictionRaceFault_Propagates_ToCaller()
    {
        var service = Create(out var querier);
        querier.ToThrow = new ObjectDisposedException("CimSession");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetStorageJobsAsync());
    }
}
