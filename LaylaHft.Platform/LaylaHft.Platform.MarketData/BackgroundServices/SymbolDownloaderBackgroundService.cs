namespace LaylaHft.Platform.MarketData.BackgroundServices;

// SymbolBackgroundService.cs

using LaylaHft.Platform.MarketData.Services;
using LaylaHft.Platform.MarketData.Services.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Diagnostics.Metrics;

public class SymbolDownloaderBackgroundService : BackgroundService
{
    private readonly ILogger<SymbolDownloaderBackgroundService> _logger;
    private readonly SymbolDownloader _downloader;
    private readonly ISymbolStore _store;
    private readonly IServiceProvider _serviceProvider;
    private readonly PeriodicTimer _timer = new(TimeSpan.FromHours(12));

    private static readonly Meter _meter = new("LaylaHft.SymbolDownloader");
    private static readonly Counter<int> _symbolDownloadCounter = _meter.CreateCounter<int>("layla_symbols_downloaded");
    private static readonly Counter<int> _symbolDownloadErrors = _meter.CreateCounter<int>("layla_symbol_download_errors");
    private static readonly Histogram<double> _symbolDownloadDuration = _meter.CreateHistogram<double>("layla_symbol_download_duration", unit: "ms");
    private static readonly ActivitySource ActivitySource = new("LaylaHft.SymbolDownloader");

    public SymbolDownloaderBackgroundService(
        ILogger<SymbolDownloaderBackgroundService> logger,
        SymbolDownloader downloader,
        ISymbolStore store,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _downloader = downloader;
        _store = store;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        do
        {
            using var activity = ActivitySource.StartActivity("DownloadSymbols.Cycle");
            var sw = Stopwatch.StartNew();

            try
            {
                _logger.LogInformation("🔽 Téléchargement des symboles en cours...");
                var count = await _downloader.LoadInitialSymbolsAsync(stoppingToken);
                _symbolDownloadCounter.Add(count);
            }
            catch (Exception ex)
            {
                _symbolDownloadErrors.Add(1);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _logger.LogError(ex, "Erreur lors du téléchargement des symboles.");
            }
            finally
            {
                sw.Stop();
                _symbolDownloadDuration.Record(sw.Elapsed.TotalMilliseconds);
            }

        } while (await _timer.WaitForNextTickAsync(stoppingToken));
    }
}