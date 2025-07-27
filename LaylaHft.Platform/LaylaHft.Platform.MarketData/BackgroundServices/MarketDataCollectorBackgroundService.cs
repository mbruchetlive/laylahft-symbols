using Binance.Net.Enums;
using Binance.Net.Interfaces;
using Binance.Net.Interfaces.Clients;
using CryptoExchange.Net.Objects.Sockets;
using FastEndpoints;
using LaylaHft.Platform.Domains;
using LaylaHft.Platform.MarketData.Events;
using LaylaHft.Platform.MarketData.Services;
using Polly;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;

namespace LaylaHft.Platform.MarketData.BackgroundServices;

public class MarketDataCollectorBackgroundService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<MarketDataCollectorBackgroundService> _logger;
    private readonly ISymbolStore _symbolStore;
    private readonly IBinanceSocketClient _socketClient;
    private readonly ICandleBufferRegistry _bufferRegistry;

    private static readonly Meter _meter = new("LaylaHft.MarketDataCollectorWorker", "1.0");
    private static readonly Counter<int> _errorCounter = _meter.CreateCounter<int>("layla_ws_errors");
    private static readonly Counter<int> _klineMessageCounter = _meter.CreateCounter<int>("layla_ws_kline_messages");
    private static readonly Histogram<double> _latencyHistogram = _meter.CreateHistogram<double>("layla_ws_candle_latency", unit: "ms");
    private static readonly ActivitySource ActivitySource = new("LaylaHft.MarketDataCollectorWorker");

    private TaskCompletionSource _initialTrigger = CreateNewTrigger();
    private TaskCompletionSource _recycleTrigger = CreateNewTrigger();

    private static TaskCompletionSource CreateNewTrigger() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _maxSymbols;
    private int _bufferWindow;

    private List<SymbolMetadata> _symbols = new();
    private List<string> _timeframes = new();

    internal List<SymbolMetadata> Symbols { get => _symbols; set => _symbols = value; }
    internal List<string> Timeframes { get => _timeframes; set => _timeframes = value; }
    internal int BufferWindow { get => _bufferWindow; set => _bufferWindow = value; }

    public MarketDataCollectorBackgroundService(
        IConfiguration configuration,
        ISymbolStore symbolStore,
        IBinanceSocketClient socketClient,
        ICandleBufferRegistry bufferRegistry,
        ILogger<MarketDataCollectorBackgroundService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _symbolStore = symbolStore;
        _socketClient = socketClient;
        _bufferRegistry = bufferRegistry;
    }

    public void NotifyStart(CancellationToken cancellationToken)
    {
        if (!_initialTrigger.Task.IsCompleted)
            _initialTrigger.SetResult();

        if (!_recycleTrigger.Task.IsCompleted)
            _recycleTrigger.SetResult();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var activity = ActivitySource.StartActivity("MarketDataCollectorWorker.ExecuteAsync");

        try
        {
            _logger.LogInformation("🕒 En attente de SymbolDownloadCompletedEvent...");
            await _initialTrigger.Task.WaitAsync(stoppingToken);

            _logger.LogInformation("🚀 Symboles téléchargés. Initialisation du MarketDataCollector...");
            LoadConfiguration();
            await LoadSymbolsAsync();
            InitializeBuffers();
            await InitializeWebSocketSubscriptionsAsync();

            var timer = new PeriodicTimer(TimeSpan.FromHours(12));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    _logger.LogInformation("🔁 Timer 12h déclenché. Attente du SymbolDownloadCompletedEvent pour redémarrage...");
                    _recycleTrigger = CreateNewTrigger();
                    await _recycleTrigger.Task.WaitAsync(stoppingToken);
                    await RestartSessionAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _errorCounter.Add(1);
                    _logger.LogError(ex, "Erreur lors du redémarrage de session 12h.");
                }
            }
        }
        catch (Exception ex)
        {
            _errorCounter.Add(1);
            _logger.LogCritical(ex, "Erreur critique dans MarketDataCollector.");
            throw;
        }
    }

    public async Task RestartSessionAsync(CancellationToken ct)
    {
        _logger.LogInformation("♻️ Redémarrage manuel de la session MarketDataCollector...");
        await _socketClient.UnsubscribeAllAsync();
        LoadConfiguration();
        await LoadSymbolsAsync();
        InitializeBuffers();
        await InitializeWebSocketSubscriptionsAsync();
        _logger.LogInformation("✅ Session WebSocket redémarrée.");
    }

    internal void LoadConfiguration()
    {
        var timeframes = _configuration.GetSection("Timeframes").Get<List<string>>();
        if (timeframes == null || timeframes.Count == 0)
            throw new InvalidOperationException("Configuration invalide : section 'Timeframes' absente ou vide.");

        Timeframes = timeframes;
        _maxSymbols = _configuration.GetValue<int?>("MaxSymbols") ?? 200;
        BufferWindow = _configuration.GetValue<int?>("BufferWindow") ?? 5;
    }

    internal async Task LoadSymbolsAsync()
    {
        Symbols = await _symbolStore.Query("binance", null, false, 1, _maxSymbols, "symbol");
        if (Symbols == null || Symbols.Count == 0)
            throw new InvalidOperationException("Aucun symbole actif trouvé.");
    }

    internal void InitializeBuffers()
    {
        foreach (var symbol in Symbols)
        {
            foreach (var tf in Timeframes)
                _bufferRegistry.InitializeBuffer(symbol.Symbol, tf, BufferWindow);
        }
    }

    internal async Task InitializeWebSocketSubscriptionsAsync()
    {
        var intervals = Timeframes.Select(ConvertToKlineInterval).Distinct().ToList();

        foreach (var chunk in Symbols.Select(s => s.Symbol).Chunk(100))
        {
            foreach (var interval in intervals)
            {
                var policy = Policy
                    .Handle<Exception>()
                    .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));

                await policy.ExecuteAsync(async () =>
                {
                    var result = await _socketClient.SpotApi.ExchangeData
                        .SubscribeToKlineUpdatesAsync(chunk, [interval], onMessage => HandleCandleUpdate(onMessage.Data));

                    if (!result.Success)
                        throw new InvalidOperationException($"\u00c9chec WS : {result.Error}");
                });
            }
        }
    }

    private static KlineInterval ConvertToKlineInterval(string tf) => tf.ToUpperInvariant() switch
    {
        "M1" => KlineInterval.OneMinute,
        "M5" => KlineInterval.FiveMinutes,
        "M15" => KlineInterval.FifteenMinutes,
        "H1" => KlineInterval.OneHour,
        "H4" => KlineInterval.FourHour,
        "D1" => KlineInterval.OneDay,
        _ => throw new ArgumentException($"Timeframe non supporté : {tf}")
    };

    internal void HandleCandleUpdate(IBinanceStreamKlineData data)
    {
        if (data?.Data == null || !data.Data.Final)
            return;

        var kline = data.Data;
        var snapshot = new CandleSnapshot(
            kline.OpenTime, kline.CloseTime, kline.OpenPrice, kline.HighPrice,
            kline.LowPrice, kline.ClosePrice, kline.Volume, data.Symbol, kline.Interval.ToString().ToUpperInvariant()
        );

        _bufferRegistry.Append(data.Symbol, kline.Interval.ToString(), snapshot);
        _ = OnCandleMessageAsync(snapshot, CancellationToken.None);
    }

    private async Task OnCandleMessageAsync(CandleSnapshot data, CancellationToken cancellationToken)
    {
        await new CandleReceivedEvent { Snapshot = data }
            .PublishAsync(Mode.WaitForNone, cancellation: cancellationToken);
    }
}