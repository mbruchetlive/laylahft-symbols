using Binance.Net.Enums;
using Binance.Net.Interfaces;
using Binance.Net.Interfaces.Clients;
using CryptoExchange.Net.Objects.Sockets;
using LaylaHft.Platform.Domains;
using LaylaHft.Platform.MarketData.Services;
using Polly;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;

namespace LaylaHft.Platform.MarketData.BackgroundServices;

/*
 * us 01 : Initialisation du Worke
 * En tant que système, je veux démarrer un service de fond qui initialise les timeframes et les symboles.

Récupération des timeframes depuis la config
Récupération de la liste des symboles via InMemorySymbolStore
Construction de l’URL WS Binance
Initialisation des buffers FIFO
Démarrage de la session WebSocket

Résumé de l’intention :
lit la configuration (Timeframes, etc.),
récupère les symboles via InMemorySymbolStore,
génère dynamiquement l’URL WebSocket Binance,
initialise les buffers,
et lance la session WebSocket.
*/

public class MarketDataCollectorWorker : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<MarketDataCollectorWorker> _logger;
    private readonly ISymbolStore _symbolStore;
    private readonly IBinanceSocketClient _socketClient;
    private readonly ICandleBufferRegistry _bufferRegistry;

    // Metrics
    private static readonly Meter _meter;
    private static readonly Counter<int> _errorCounter;
    private static readonly Counter<int> _klineMessageCounter;
    private static readonly Histogram<double> _latencyHistogram;
    static MarketDataCollectorWorker()
    {
        // Initialize the Meter for metrics collection
        _meter = new Meter("LaylaHft.MarketDataCollectorWorker", "1.0");

        _errorCounter = _meter.CreateCounter<int>("layla_ws_errors");
        _klineMessageCounter = _meter.CreateCounter<int>("layla_ws_kline_messages");
        _latencyHistogram = _meter.CreateHistogram<double>("layla_ws_candle_latency", unit: "ms");
    }

    private static readonly ActivitySource ActivitySource = new("LaylaHft.MarketDataCollectorWorker");

    private int _maxSymbols;
    private int _bufferWindow;


    private List<SymbolMetadata> _symbols;
    private List<string> _timeframes = [];

    internal List<SymbolMetadata> Symbols { get => _symbols; set => _symbols = value; }
    internal List<string> Timeframes { get => _timeframes; set => _timeframes = value; }
    internal int BufferWindow { get => _bufferWindow; set => _bufferWindow = value; }

    public MarketDataCollectorWorker(
        IConfiguration configuration,
        ISymbolStore symbolStore,
        IBinanceSocketClient socketClient,
        ICandleBufferRegistry bufferRegistry,
        ILogger<MarketDataCollectorWorker> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _symbolStore = symbolStore ?? throw new ArgumentNullException(nameof(symbolStore));
        _socketClient = socketClient ?? throw new ArgumentNullException(nameof(socketClient));
        _bufferRegistry = bufferRegistry ?? throw new ArgumentNullException(nameof(bufferRegistry));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var activity = ActivitySource.StartActivity("MarketDataCollectorWorker.ExecuteAsync");

        try
        {
            LoadConfiguration();
            await LoadSymbolsAsync();
            InitializeBuffers();
            await InitializeWebSocketSubscriptionsAsync();
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (Exception ex)
        {
            _errorCounter.Add(1);
            _logger.LogCritical(ex, "Erreur critique lors de l'initialisation du MarketDataCollector.");
            throw;
        }
    }

    internal void LoadConfiguration()
    {
        var timeframes = _configuration.GetSection("Timeframes").Get<List<string>>();
        if (timeframes == null || timeframes.Count == 0)
        {
            _logger.LogError("Aucun timeframe n'a été défini dans la configuration.");
            throw new InvalidOperationException("Configuration invalide : section 'Timeframes' absente ou vide.");
        }

        Timeframes = timeframes;
        _maxSymbols = _configuration.GetValue<int?>("MaxSymbols") ?? 200;
        BufferWindow = _configuration.GetValue<int?>("BufferWindow") ?? 5;

        _logger.LogInformation("Timeframes configurés : {Timeframes}", string.Join(", ", Timeframes));
        _logger.LogInformation("MaxSymbols = {MaxSymbols} | BufferWindow = {BufferWindow}", _maxSymbols, BufferWindow);
    }

    internal async Task LoadSymbolsAsync()
    {
        using var activity = ActivitySource.StartActivity("LoadSymbols");
        try
        {
            _logger.LogInformation("Chargement des symboles actifs depuis le InMemorySymbolStore...");
            Symbols = await _symbolStore.Query(
                exchange: "binance",
                quoteClass: null,
                includeInactive: false,
                page: 1,
                pageSize: _maxSymbols,
                sortBy: "symbol"
            );

            if (Symbols == null || Symbols.Count == 0)
            {
                _logger.LogCritical("Aucun symbole actif récupéré. Arrêt du service.");
                throw new InvalidOperationException("Aucun symbole actif trouvé.");
            }

            Symbols = Symbols;
            _logger.LogInformation("{Count} symboles actifs chargés.", Symbols.Count);
        }
        catch (Exception ex)
        {
            _errorCounter.Add(1);
            _logger.LogCritical(ex, "Échec lors de la récupération des symboles.");
            throw;
        }
    }

    internal void InitializeBuffers()
    {
        using var activity = ActivitySource.StartActivity("InitializeBuffers");
        _logger.LogInformation("Initialisation des buffers circulaires pour les symboles et timeframes...");

        foreach (var symbol in Symbols)
        {
            foreach (var tf in Timeframes)
            {
                var interval = ConvertToKlineInterval(tf);
                _bufferRegistry.InitializeBuffer(symbol.Symbol, interval, BufferWindow);
            }
        }
    }

    internal async Task InitializeWebSocketSubscriptionsAsync()
    {
        using var activity = ActivitySource.StartActivity("InitializeWebSocketSubscriptions");
        var intervals = Timeframes
            .Select(ConvertToKlineInterval)
            .Distinct()
            .ToList();

        foreach (var chunk in Symbols.Select(s => s.Symbol).Chunk(100))
        {
            var policy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    (ex, delay, attempt, ctx) =>
                    {
                        _errorCounter.Add(1);
                        _logger.LogWarning(ex, "Retry {Attempt}/3 après erreur de souscription : {Message}", attempt, ex.Message);
                    });

            await policy.ExecuteAsync(async () =>
            {
                var result = await _socketClient.SpotApi.ExchangeData
                    .SubscribeToKlineUpdatesAsync(chunk, intervals, onMessage => HandleCandleUpdate(onMessage.Data));

                if (!result.Success)
                {
                    throw new InvalidOperationException($"Échec WS : {result.Error}");
                }

                _logger.LogInformation("Souscription réussie pour {Count} symboles sur {CountIntervals} timeframes",
                    chunk.Count(), intervals.Count);
            });
        }
    }

    private static KlineInterval ConvertToKlineInterval(string tf)
    {
        return tf.ToUpperInvariant() switch
        {
            "M1" => KlineInterval.OneMinute,
            "M3" => KlineInterval.ThreeMinutes,
            "M5" => KlineInterval.FiveMinutes,
            "M15" => KlineInterval.FifteenMinutes,
            "M30" => KlineInterval.ThirtyMinutes,
            "H1" => KlineInterval.OneHour,
            "H2" => KlineInterval.TwoHour,
            "H4" => KlineInterval.FourHour,
            "H6" => KlineInterval.SixHour,
            "H8" => KlineInterval.EightHour,
            "H12" => KlineInterval.TwelveHour,
            "D1" => KlineInterval.OneDay,
            "D3" => KlineInterval.ThreeDay,
            "W1" => KlineInterval.OneWeek,
            "MN" => KlineInterval.OneMonth,
            _ => throw new ArgumentException($"Timeframe non supporté : {tf}")
        };
    }

    internal void HandleCandleUpdate(IBinanceStreamKlineData data)
    {
        var sw = Stopwatch.StartNew();
        using var activity = ActivitySource.StartActivity("HandleCandleUpdate");

        if(data == null || data.Data == null)
        {
            _logger.LogWarning("Données de bougie nulles ou vides reçues.");
            return;
        }

        if (!data.Data.Final)
        {
            _logger.LogDebug("Mise à jour de bougie intermédiaire pour {Symbol} à {CloseTime}", data.Symbol, data.Data.CloseTime);
        }
        else
        {
            _logger.LogDebug("Bougie finale reçue pour {Symbol} à {CloseTime}", data.Symbol, data.Data.CloseTime);

            var kline = data.Data;
            var snapshot = new CandleSnapshot(
                openTime: kline.OpenTime,
                closeTime: kline.CloseTime,
                open: kline.OpenPrice,
                high: kline.HighPrice,
                low: kline.LowPrice,
                close: kline.ClosePrice,
                volume: kline.Volume,
                symbol: data.Symbol,
                interval: kline.Interval.ToString().ToUpperInvariant()
            );

            _bufferRegistry.Append(data.Symbol, kline.Interval, snapshot);
            _klineMessageCounter.Add(1);
            sw.Stop();
            _latencyHistogram.Record(sw.Elapsed.TotalMilliseconds);

            _logger.LogInformation("Kline reçu : {Symbol} | {Interval} | Close: {Close} | TS: {TS}",
                data.Symbol,
                kline.Interval,
                kline.ClosePrice,
                kline.CloseTime);

            _ = OnCandleMessageAsync(snapshot, CancellationToken.None);
        }
    }

    private async Task OnCandleMessageAsync(CandleSnapshot data, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("OnCandleMessage");
        await Task.Yield();
        _logger.LogDebug("Message reçu pour {Symbol} à {CloseTime}", data.Symbol, data.CloseTime);
    }
}
