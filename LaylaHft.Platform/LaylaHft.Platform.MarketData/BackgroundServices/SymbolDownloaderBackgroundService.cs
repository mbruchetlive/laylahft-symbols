namespace LaylaHft.Platform.MarketData.BackgroundServices;

// SymbolBackgroundService.cs

using LaylaHft.Platform.MarketData.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public class SymbolDownloaderBackgroundService : BackgroundService
{
    private readonly SymbolDownloader _downloader;
    private readonly ILogger<SymbolDownloaderBackgroundService> _logger;
    private readonly TimeSpan _refreshInterval = TimeSpan.FromHours(12);

    public SymbolDownloaderBackgroundService(SymbolDownloader downloader, ILogger<SymbolDownloaderBackgroundService> logger)
    {
        _downloader = downloader;
        _logger = logger;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[SymbolDownloaderBackground] Initial load started...");
        try
        {
            await _downloader.LoadInitialSymbolsAsync(cancellationToken);
            _logger.LogInformation("[SymbolDownloaderBackground] Initial load completed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SymbolDownloaderBackground] Initial load failed.");
        }
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("[SymbolDownloaderBackground] Scheduled refresh started...");
            try
            {
                await _downloader.LoadInitialSymbolsAsync(cancellationToken);
                _logger.LogInformation("[SymbolDownloaderBackground] Refresh completed.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SymbolDownloaderBackground] Refresh failed.");
            }
            await Task.Delay(_refreshInterval, cancellationToken);
        }
    }
}