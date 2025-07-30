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
            _logger.LogInformation("🕒 En attente de SymbolDownloadedEvent...");
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
                    _logger.LogInformation("🔁 Timer 12h déclenché. Attente du SymbolDownloadedEvent pour redémarrage...");
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

    private static string ConvertFromKlineInterval(KlineInterval interval) => interval switch
    {
        KlineInterval.OneMinute => "M1",
        KlineInterval.ThreeMinutes => "M3",
        KlineInterval.FiveMinutes => "M5",
        KlineInterval.FifteenMinutes => "M15",
        KlineInterval.ThirtyMinutes => "M30",
        KlineInterval.OneHour => "H1",
        KlineInterval.TwoHour => "H2",
        KlineInterval.FourHour => "H4",
        KlineInterval.SixHour => "H6",
        KlineInterval.EightHour => "H8",
        KlineInterval.TwelveHour => "H12",
        KlineInterval.OneDay => "D1",
        KlineInterval.ThreeDay => "D3",
        KlineInterval.OneWeek => "W1",
        KlineInterval.OneMonth => "MN",
        _ => throw new ArgumentOutOfRangeException(nameof(interval), $"Interval non supporté : {interval}")
    };

    internal void HandleCandleUpdate(IBinanceStreamKlineData data)
    {
        if (data?.Data == null)
            return;

        var kline = data.Data;

        if (string.IsNullOrEmpty(data.Symbol))
        {
            _logger.LogWarning("Données de bougie invalide reçues - symbole non identifiable : {Data}", data);
            return;
        }

        if (kline.ClosePrice <= 0 || kline.Volume <= 0)
        {
            _logger.LogDebug("Bougie invalide reçue prix ou volume invalide ou égale à 0 : {Kline}", kline);
            return;
        }

        var tf = ConvertFromKlineInterval(kline.Interval);

        // 📌 Calcul de pondération pour bougie en cours
        double maturityFactor = 1.0;
        var totalSeconds = (kline.CloseTime - kline.OpenTime).TotalSeconds;
        var elapsedSeconds = (DateTime.UtcNow - kline.OpenTime).TotalSeconds;

        maturityFactor = Math.Clamp(elapsedSeconds / totalSeconds, 0.0, 1.0);

        _logger.LogDebug("Bougie {Symbol} ({TF}) - Maturité {Weight:P2} CloseTime={CloseTime}",
            data.Symbol, tf, maturityFactor, kline.CloseTime);

        var snapshot = new CandleSnapshot(
            kline.OpenTime, kline.CloseTime, kline.OpenPrice, kline.HighPrice,
            kline.LowPrice, kline.ClosePrice, kline.Volume, data.Symbol, tf
        )
        {
            Weight = maturityFactor
        };

        // 📌 Le buffer décide si c’est une mise à jour partielle ou une clôture
        var closedCandle = _bufferRegistry.UpdatePartialCandle(data.Symbol, tf, snapshot);

        // 📢 Si le buffer retourne une bougie close → publication
        if (closedCandle != null)
        {
            _logger.LogInformation("Bougie close détectée et publiée {Symbol} {TF} - Close={Close}",
                closedCandle.Symbol, closedCandle.Interval, closedCandle.Close);

            _bufferRegistry.Append(data.Symbol, tf, closedCandle);
            _ = OnCandleMessageAsync(closedCandle, CancellationToken.None);
        }
    }



    private async Task OnCandleMessageAsync(CandleSnapshot data, CancellationToken cancellationToken)
    {
        await new CandleReceivedEvent { Snapshot = data }
            .PublishAsync(Mode.WaitForNone, cancellation: cancellationToken);
    }
}