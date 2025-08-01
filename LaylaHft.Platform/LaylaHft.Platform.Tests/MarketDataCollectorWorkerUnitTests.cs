namespace LaylaHft.Platform.Tests;

using Binance.Net.Enums;
using Binance.Net.Interfaces;
using Binance.Net.Interfaces.Clients;
using Binance.Net.Objects.Models.Spot.Socket;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Sockets;
using LaylaHft.Platform.Domains;
using LaylaHft.Platform.MarketData.BackgroundServices;
using LaylaHft.Platform.MarketData.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using System.Diagnostics.Metrics;
using System.Threading;
using Xunit;

public class TestMeterFactory : IMeterFactory
{
    public Meter Create(MeterOptions options) => new Meter(options.Name);

    public void Dispose()
    {
    }
}

public class MarketDataCollectorWorkerUnitTests
{
    private readonly Mock<IConfiguration> _mockConfig = new();
    private readonly Mock<ISymbolStore> _mockSymbolStore = new();
    private readonly Mock<IBinanceSocketClient> _mockSocketClient = new();
    private readonly Mock<ICandleBufferRegistry> _mockBufferRegistry = new();
    private readonly Mock<ILogger<MarketDataCollectorBackgroundService>> _mockLogger = new();

    public MarketDataCollectorWorkerUnitTests()
    {
        var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddMeter("LaylaHft.InMemorySymbolStore")
            .AddMeter("LaylaHft.MarketStats")
            .AddMeter("LaylaHft.MarketDataCollectorWorker")
            .AddConsoleExporter()
            .Build();
    }

    [Fact]
    public async Task ExecuteAsync_Throws_WhenTimeframesMissing()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var worker = new MarketDataCollectorBackgroundService(config, _mockSymbolStore.Object, _mockSocketClient.Object, _mockBufferRegistry.Object, _mockLogger.Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LoadSymbolsAsync_Throws_WhenNoSymbolsReturned()
    {
        var dict = new Dictionary<string, string?> { ["Timeframes:0"] = "M1" };
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        _mockSymbolStore.Setup(s => s.Query(It.IsAny<string>(), null, false, 1, It.IsAny<int>(), "symbol"))
                        .ReturnsAsync([]);

        var worker = new MarketDataCollectorBackgroundService(config, _mockSymbolStore.Object, _mockSocketClient.Object, _mockBufferRegistry.Object, _mockLogger.Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.StartAsync(CancellationToken.None));
    }

    [Fact]
    public void InitializeBuffers_CreatesExpectedBuffers()
    {
        var dict = new Dictionary<string, string?> { ["Timeframes:0"] = "M1" };
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var symbols = new List<SymbolMetadata> { new() { Symbol = "BTCUSDT" }, new() { Symbol = "ETHUSDT" } };
        var worker = new MarketDataCollectorBackgroundService(config, _mockSymbolStore.Object, _mockSocketClient.Object, _mockBufferRegistry.Object, _mockLogger.Object);

        worker.Symbols = symbols;
        worker.Timeframes = ["M1"];
        worker.BufferWindow = 5;

        worker.InitializeBuffers();

        _mockBufferRegistry.Verify(x => x.InitializeBuffer("BTCUSDT", "M1", 5), Times.Once);
        _mockBufferRegistry.Verify(x => x.InitializeBuffer("ETHUSDT", "M1", 5), Times.Once);
    }

    [Fact]
    public async Task WebSocketSubscription_Warns_WhenSubscriptionFails()
    {
        var dict = new Dictionary<string, string?> { ["Timeframes:0"] = "M1" };
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var symbols = Enumerable.Range(0, 200).Select(i => new SymbolMetadata { Symbol = $"SYM{i}" }).ToList();

        _mockSocketClient.Setup(c => c.SpotApi.ExchangeData.SubscribeToKlineUpdatesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<KlineInterval>>(),
            It.IsAny<Action<DataEvent<IBinanceStreamKlineData>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new CallResult<UpdateSubscription>(null, null, new ClientRateLimitError("client rate limit")));

        var worker = new MarketDataCollectorBackgroundService(config, _mockSymbolStore.Object, _mockSocketClient.Object, _mockBufferRegistry.Object, _mockLogger.Object);

        worker.Symbols = symbols;
        worker.Timeframes = ["M1"];

        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.InitializeWebSocketSubscriptionsAsync());
    }

    [Fact]
    public async Task WebSocketSubscription_Success_LogsInformation()
    {
        var dict = new Dictionary<string, string?> { ["Timeframes:0"] = "M1" };
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var symbols = Enumerable.Range(0, 100).Select(i => new SymbolMetadata { Symbol = $"SYM{i}" }).ToList();

        var result = CallResult<UpdateSubscription>.SuccessResult;

        _mockSocketClient.Setup(c => c.SpotApi.ExchangeData.SubscribeToKlineUpdatesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<KlineInterval>>(),
            It.IsAny<Action<DataEvent<IBinanceStreamKlineData>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new CallResult<UpdateSubscription>(default(UpdateSubscription)));

        var worker = new MarketDataCollectorBackgroundService(config, _mockSymbolStore.Object, _mockSocketClient.Object, _mockBufferRegistry.Object, _mockLogger.Object)
        {
            Symbols = symbols,
            Timeframes = ["M1"]
        };

        await worker.InitializeWebSocketSubscriptionsAsync();

        _mockLogger.Verify(x => x.Log(LogLevel.Information, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), null, It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
    }

    [Fact]
    public void HandleCandleUpdate_AppendsBufferAndLogs()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Timeframes:0"] = "M1" }).Build();
        var worker = new MarketDataCollectorBackgroundService(config, _mockSymbolStore.Object, _mockSocketClient.Object, _mockBufferRegistry.Object, _mockLogger.Object);

        var stream = new BinanceStreamKlineData
        {
            Data = new BinanceStreamKline
            {
                Interval = KlineInterval.OneMinute,
                OpenTime = DateTime.UtcNow,
                CloseTime = DateTime.UtcNow,
                OpenPrice = 10,
                HighPrice = 12,
                LowPrice = 8,
                ClosePrice = 11,
                Volume = 100
            },
            Symbol = "BTCUSDT"
        };

        worker.HandleCandleUpdate(stream);

        _mockBufferRegistry.Verify(x => x.Append("BTCUSDT", KlineInterval.OneMinute.ToString(), It.IsAny<CandleSnapshot>()), Times.Once);
        _mockLogger.Verify(x => x.Log(LogLevel.Information, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), null, It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
    }
}