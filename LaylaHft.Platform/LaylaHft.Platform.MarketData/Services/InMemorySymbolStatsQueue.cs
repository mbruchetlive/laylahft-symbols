using LaylaHft.Platform.MarketData.Events;
using System.Threading.Channels;

namespace LaylaHft.Platform.MarketData.Services;

public class InMemorySymbolStatsQueue : ISymbolStatsQueue
{
    private readonly Channel<SymbolImportedEvent> _channel = Channel.CreateUnbounded<SymbolImportedEvent>();

    public Task EnqueueAsync(SymbolImportedEvent e) => _channel.Writer.WriteAsync(e).AsTask();

    public ValueTask<SymbolImportedEvent> DequeueAsync(CancellationToken ct) =>
        _channel.Reader.ReadAsync(ct);
}
