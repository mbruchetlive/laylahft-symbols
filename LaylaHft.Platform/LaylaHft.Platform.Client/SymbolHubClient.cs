namespace LaylaHft.Platform.Client;

using LaylaHft.Platform.Domains;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

public class SymbolUpdateClient
{
    private readonly ILogger<SymbolUpdateClient> _logger;
    private readonly HubConnection _connection;
    public event EventHandler<SymbolMetadata> SymbolUpdated;

    public SymbolUpdateClient(string hubUrl, ILogger<SymbolUpdateClient> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();

        _connection.On<SymbolMetadata>("SymbolUpdated", symbol =>
        {
            logger.LogInformation($"[SymbolUpdated] {symbol.Symbol} - {symbol.BaseAsset}/{symbol.QuoteAsset}");
            SymbolUpdated?.Invoke(this, symbol);
        });
    }

    public async Task StartAsync()
    {
        try
        {
            await _connection.StartAsync();
            _logger.LogInformation("✅ Connected to SymbolHub");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"❌ Failed to connect to SymbolHub: {ex.Message}");
        }
    }

    public async Task StopAsync()
    {
        await _connection.StopAsync();
        _logger.LogWarning("🛑 Disconnected from SymbolHub");
    }
}