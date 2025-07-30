using LaylaHft.Platform.Domains;
using System.Threading.Channels;

namespace LaylaHft.Platform.MarketData.Services;

public class InMemorySymbolStatsQueue : ISymbolStatsQueue
{
    private readonly Channel<SymbolMetadata> _channel = Channel.CreateUnbounded<SymbolMetadata>();

    private int _enqueuedCount = 0;
    private int _processedCount = 0;
    private TaskCompletionSource _allProcessedTcs = CreateNewTcs();

    private static TaskCompletionSource CreateNewTcs() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task EnqueueAsync(SymbolMetadata e)
    {
        Interlocked.Increment(ref _enqueuedCount);
        return _channel.Writer.WriteAsync(e).AsTask();
    }

    public ValueTask<SymbolMetadata> DequeueAsync(CancellationToken ct) =>
        _channel.Reader.ReadAsync(ct);

    public void MarkProcessed()
    {
        var processed = Interlocked.Increment(ref _processedCount);
        var enqueued = Volatile.Read(ref _enqueuedCount);

        if (processed == enqueued)
        {
            _allProcessedTcs.TrySetResult();
        }
    }

    public Task WaitForProcessingAsync(CancellationToken cancellationToken) =>
        _allProcessedTcs.Task.WaitAsync(cancellationToken);

    public void Reset()
    {
        Interlocked.Exchange(ref _enqueuedCount, 0);
        Interlocked.Exchange(ref _processedCount, 0);
        _allProcessedTcs = CreateNewTcs();
    }
}
