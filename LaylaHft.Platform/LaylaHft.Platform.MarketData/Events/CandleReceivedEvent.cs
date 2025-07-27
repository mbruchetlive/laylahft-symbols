using FastEndpoints;
using LaylaHft.Platform.Domains;

namespace LaylaHft.Platform.MarketData.Events;

public class CandleReceivedEvent : IEvent
{
    public CandleSnapshot Snapshot { get; set; }
}
