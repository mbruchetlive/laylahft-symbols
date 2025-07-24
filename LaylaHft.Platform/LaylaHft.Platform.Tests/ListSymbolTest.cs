using LaylaHft.Platform.Client;
using LaylaHft.Platform.Shared;
using Microsoft.Extensions.Logging;
using System.Threading;

public class MarketDataIntegrationTests
{
    private readonly ITestOutputHelper _output;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public MarketDataIntegrationTests(ITestOutputHelper  output)
    {
        _output = output;
    }

    [Fact]
    public async Task QuerySymbols_Returns_ValidResponse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.LaylaHft_Platform_MarketData>(cancellationToken);

        appHost.Services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Debug);
            logging.AddXUnit(_output);
            logging.AddFilter("LaylaHft", LogLevel.Debug);
            logging.AddFilter("Aspire", LogLevel.Debug);
        });

        appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
        {
            clientBuilder.AddStandardResilienceHandler();
        });

        await using var app = await appHost.BuildAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        await app.StartAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);

        var http = app.CreateHttpClient("laylahft-platform-marketdata", "laylahft-platform-marketdata-https");

        var client = new MarketDataClient(
            http,
            email: "my-client-id",
            password: "my-client-secret",
            scopes: new[] { "symbols:read" }
        );

        var query = new ListSymbolsQuery
        {
            Exchange = "BINANCE",
            QuoteClass = "USDC",
            Currency = "USDC",
            IncludeInactive = false,
            Page = 1,
            PageSize = 20,
            SortBy = "symbol"
        };

        var pollInterval = TimeSpan.FromSeconds(5);
        var timeout = TimeSpan.FromMinutes(5);

        await Task.Delay(pollInterval, cancellationToken);

        var deadline = DateTime.UtcNow + timeout;

        var result = await client.GetSymbolsAsync(query, cancellationToken);

        if (result?.Symbols?.Count < 1)
        {
            while (DateTime.UtcNow < deadline)
            {
                result = await client.GetSymbolsAsync(query, cancellationToken);

                if (result?.Symbols?.Any() == true)
                {
                    break;
                }

                await Task.Delay(pollInterval, cancellationToken);
            }
        }

        // Logging (facultatif, utile pour CI ou debug pipeline)
        _output.WriteLine($"✔ Token: {result?.Message}");
        _output.WriteLine($"✔ Symbols Count: {result?.TotalCount}");

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.True(result.TotalCount >= 0);
        Assert.NotEmpty(result.Symbols);
    }
}
