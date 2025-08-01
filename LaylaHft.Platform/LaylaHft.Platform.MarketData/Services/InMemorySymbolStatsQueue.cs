using LaylaHft.Platform.Domains;
using LaylaHft.Platform.MarketData.Services.Interfaces;
using System.Diagnostics.Metrics;
using System.Threading.Channels;

namespace LaylaHft.Platform.MarketData.Services;

public class InMemorySymbolStatsQueue : ISymbolStatsQueue
{
    private readonly Channel<SymbolMetadata> _channel = Channel.CreateUnbounded<SymbolMetadata>();

    private TaskCompletionSource _allProcessedTcs = CreateNewTcs();

    private static int _enqueuedCount = 0;
    private static int _processedCount = 0;
    private static readonly Meter _meter = new("LaylaHft.SymbolStatsQueue");
    private static readonly ObservableCounter<int> _enqueuedCounter;
    private static readonly ObservableCounter<int> _processedCounter;

    private static TaskCompletionSource CreateNewTcs() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    static InMemorySymbolStatsQueue()
    {
        _enqueuedCounter = _meter.CreateObservableCounter(
            name: "laylahft.symbolstatsqueue.enqueued.total",
            observeValue: () => Volatile.Read(ref _enqueuedCount),
            description: "Total number of items enqueued in the Symbol Stats queue"
        );

        _processedCounter = _meter.CreateObservableCounter(
            name: "laylahft.symbolstatsqueue.processed.total",
            observeValue: () => Volatile.Read(ref _processedCount),
            description: "Total number of items processed from the Symbol Stats queue"
        );
    }

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
