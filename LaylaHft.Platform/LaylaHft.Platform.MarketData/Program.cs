using FastEndpoints;
using FastEndpoints.Security;
using FastEndpoints.Swagger;
using LaylaHft.Platform.MarketData;
using LaylaHft.Platform.MarketData.BackgroundServices;
using LaylaHft.Platform.MarketData.Options;
using LaylaHft.Platform.MarketData.Services;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("LaylaHft.Platform.Tests")]

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var jwtKey = builder.Configuration["Jwt:SigningKey"];

ArgumentException.ThrowIfNullOrEmpty(jwtKey, "Jwt:Signing is null");

builder.Services
    .AddAuthenticationJwtBearer(s =>
    {
        s.SigningKey = jwtKey;
    }, options =>
    {
        options.RequireHttpsMetadata = false;
    }) //add this
   .AddAuthorization()
   .AddFastEndpoints()
   .SwaggerDocument(o =>
    {
        o.DocumentSettings = s =>
        {
            s.Title = "Layla HFT - Symbols api";
            s.Version = "v1";
        };
    });

builder.Services.AddBinance();

builder.Services.Configure<MarketDetectionSettings>(
    builder.Configuration.GetSection("MarketDetection"));

builder.Services.AddSingleton<ISymbolStore>(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var logger = sp.GetRequiredService<ILogger<InMemorySymbolStore>>();
    var path = Path.Combine(env.ContentRootPath, "Data", "symbols.bin");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    return new InMemorySymbolStore(path, logger);
});

builder.Services.AddSingleton<SymbolDownloader>();
builder.Services.AddSingleton<IMyAuthService, MyAuthService>();
builder.Services.AddSingleton<ISymbolMarketStatsCalculator, InMemorySymbolMarketStatsCalculator>();
builder.Services.AddSingleton<ISymbolStatsQueue, InMemorySymbolStatsQueue>();
builder.Services.AddSingleton<ICandleBufferRegistry, InMemoryCandleBufferRegistry>();

builder.Services.AddHostedService<SymbolDownloaderBackgroundService>();
builder.Services.AddHostedService<SymbolStatsProcessorService>();
builder.Services.AddHostedService<MarketDataCollectorBackgroundService>();

builder.Services.AddSignalR();

var app = builder.Build();

app.UseAuthentication()
   .UseAuthorization()
   .UseFastEndpoints()
   .UseSwaggerGen();

app.MapHub<SymbolHub>("/hubs/symbols");

app.Run();