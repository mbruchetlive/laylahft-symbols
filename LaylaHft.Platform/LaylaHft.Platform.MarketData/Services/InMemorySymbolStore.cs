using LaylaHft.Platform.Domains;
using LaylaHft.Platform.MarketData.Services.Interfaces;
using MessagePack;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;

namespace LaylaHft.Platform.MarketData.Services;

public class InMemorySymbolStore : ISymbolStore, IDisposable
{
    private readonly ConcurrentDictionary<string, SymbolMetadata> _symbols = new();
    private readonly string _storagePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly ILogger<InMemorySymbolStore> _logger;

    private static readonly Counter<int> _symbolUpsertCounter;
    private static readonly Counter<int> _symbolQueryCounter;
    private static readonly Histogram<double> _queryDurationHistogram;
    private static readonly ActivitySource _activitySource;
    
    
    private readonly Timer _flushTimer;
    private volatile bool _hasPendingChanges = false;
    private readonly TimeSpan _flushInterval = TimeSpan.FromMinutes(5);

    static InMemorySymbolStore()
    {
        // Register the ActivitySource for telemetry
        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
        Activity.ForceDefaultIdFormat = true;

        var meter = new Meter("LaylaHft.InMemorySymbolStore");
        _activitySource = new ActivitySource("LaylaHft.InMemorySymbolStore");
        
        _symbolUpsertCounter = meter.CreateCounter<int>("symbols.upsert.count", description: "Total number of symbol upserts");
        _symbolQueryCounter = meter.CreateCounter<int>("symbols.query.count", description: "Total number of symbol queries");
        _queryDurationHistogram = meter.CreateHistogram<double>("symbols.query.duration.ms", unit: "ms", description: "Duration of symbol queries");

    }

    public InMemorySymbolStore(string storagePath, ILogger<InMemorySymbolStore> logger)
    {
        _storagePath = storagePath;
        _logger = logger;


        LoadFromFile();
        _flushTimer = new Timer(_ => FlushIfNeededAsync().GetAwaiter().GetResult(), null, _flushInterval, _flushInterval);
    }

    private async void LoadFromFile()
    {
        try
        {
            if (!File.Exists(_storagePath))
            {
                _logger.LogInformation("Aucun fichier de symboles à charger ({Path})", _storagePath);
                return;
            }

            if (File.Exists(_storagePath))
            {
                var bytes = await File.ReadAllBytesAsync(_storagePath);
                var items = MessagePackSerializer.Deserialize<List<KeyValuePair<string, SymbolMetadata>>>(bytes);

                _symbols.Clear();

                foreach (var kvp in items)
                    _symbols[kvp.Key] = kvp.Value;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du chargement des symboles depuis le fichier {Path}", _storagePath);
        }
    }

    public async Task SaveToFileAsync()
    {
        await _fileLock.WaitAsync();
        try
        {
            var data = _symbols.Select(kvp => new KeyValuePair<string, SymbolMetadata>(kvp.Key, kvp.Value)).ToList();
            var bytes = MessagePackSerializer.Serialize(data);

            await File.WriteAllBytesAsync(_storagePath, bytes);

            _logger.LogInformation("Fichier des symboles sauvegardé ({Count} items)", data.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la sauvegarde du fichier des symboles");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task FlushIfNeededAsync()
    {
        if (!_hasPendingChanges)
            return;

        await SaveToFileAsync();
        _hasPendingChanges = false;
    }

    public async Task Upsert(string? exchange, string? quoteClass, string symbol, SymbolMetadata metadata)
    {
        using var activity = _activitySource.StartActivity("InMemorySymbolStore.Upsert");

        var key = $"{exchange?.ToLowerInvariant()}|{quoteClass?.ToLowerInvariant()}|{symbol.ToLowerInvariant()}";
        _symbols[key] = metadata;

        _symbolUpsertCounter.Add(1);
        _logger.LogDebug("Upsert de {Symbol} effectué pour {Exchange}/{QuoteClass}", symbol, exchange, quoteClass);

        _hasPendingChanges = true;
    }

    public Task<int> Count(string? exchange, string? quoteClass, bool includeInactive)
    {
        using var activity = _activitySource.StartActivity("InMemorySymbolStore.Count");

        var query = _symbols.Values.AsEnumerable();

        if (!string.IsNullOrEmpty(exchange))
            query = query.Where(s => s.Exchange?.Equals(exchange, StringComparison.OrdinalIgnoreCase) == true);

        if (!string.IsNullOrEmpty(quoteClass))
            query = query.Where(s => s.QuoteAsset?.Equals(quoteClass, StringComparison.OrdinalIgnoreCase) == true);

        if (includeInactive)
            query = query.Where(s => s.Status == SymbolStatus.Inactive || s.Status == SymbolStatus.Active);

        var count = query.Count();
        _logger.LogDebug("Count effectué : {Count} symboles filtrés", count);

        return Task.FromResult(count);
    }

    public Task<List<SymbolMetadata>> Query(string? exchange, string? quoteClass, bool includeInactive, int page, int pageSize, string? sortBy)
    {
        using var activity = _activitySource.StartActivity("InMemorySymbolStore.Query");
        var sw = Stopwatch.StartNew();

        var query = _symbols.Values.AsEnumerable();

        if (!string.IsNullOrEmpty(exchange))
            query = query.Where(s => s.Exchange?.Equals(exchange, StringComparison.OrdinalIgnoreCase) == true);

        if (!string.IsNullOrEmpty(quoteClass))
            query = query.Where(s => s.QuoteAsset?.Equals(quoteClass, StringComparison.OrdinalIgnoreCase) == true);

        if (includeInactive)
            query = query.Where(s => s.Status == SymbolStatus.Inactive || s.Status == SymbolStatus.Active);

        query = !string.IsNullOrEmpty(sortBy) ? sortBy.ToLowerInvariant() switch
        {
            "symbol" => query.OrderBy(s => s.Symbol),
            "name" => query.OrderBy(s => s.Name),
            "exchange" => query.OrderBy(s => s.Exchange),
            "baseasset" => query.OrderBy(s => s.BaseAsset),
            "quoteasset" => query.OrderBy(s => s.QuoteAsset),
            _ => query
        } : query.OrderBy(s => s.Symbol);

        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 100;

        var result = query.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        sw.Stop();
        _symbolQueryCounter.Add(1);
        _queryDurationHistogram.Record(sw.Elapsed.TotalMilliseconds);

        _logger.LogInformation("Query de symboles : {Count} résultats en {Elapsed}ms", result.Count, sw.Elapsed.TotalMilliseconds);

        return Task.FromResult(result);
    }

    public void Dispose()
    {
        _flushTimer?.Dispose();
        FlushIfNeededAsync().GetAwaiter().GetResult();
    }

    public Task<SymbolMetadata?> GetAsync(string exchange, string quoteClass, string symbol)
    {
        var key = $"{exchange?.ToLowerInvariant()}|{quoteClass?.ToLowerInvariant()}|{symbol.ToLowerInvariant()}";
        return Task.FromResult(_symbols.TryGetValue(key, out var metadata) ? metadata : null);
    }
}
