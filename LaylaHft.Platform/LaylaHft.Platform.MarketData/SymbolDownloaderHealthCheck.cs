using LaylaHft.Platform.MarketData.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LaylaHft.Platform.MarketData;

public class SymbolDownloaderHealthCheck : IHealthCheck
{
    private readonly SymbolDownloader _downloader;

    public SymbolDownloaderHealthCheck(SymbolDownloader downloader)
    {
        _downloader = downloader;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (_downloader.IsOnline)
        {
            return Task.FromResult(HealthCheckResult.Degraded("SymbolDownloader is connected to Binance."));
        }

        return Task.FromResult(HealthCheckResult.Healthy("SymbolDownloader is in FAILBACK MODE — Binance unreachable."));
    }
}