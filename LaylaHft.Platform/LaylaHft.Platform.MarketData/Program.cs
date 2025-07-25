using FastEndpoints;
using FastEndpoints.Security;
using FastEndpoints.Swagger;
using LaylaHft.Platform.MarketData;
using LaylaHft.Platform.MarketData.Services;
using System.Diagnostics.Metrics;

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

builder.Services.AddSingleton<ISymbolStore>(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var logger = sp.GetRequiredService<ILogger<SymbolStore>>();
    var meters = sp.GetRequiredService<IMeterFactory>();
    var path = Path.Combine(env.ContentRootPath, "Data", "symbols.bin");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    return new SymbolStore(path, logger, meters);
});

builder.Services.AddSingleton<SymbolDownloader>();
builder.Services.AddSingleton<IMyAuthService, MyAuthService>();

builder.Services.AddHostedService<SymbolDownloaderBackgroundService>();

builder.Services.AddSignalR();

var app = builder.Build();

app.UseAuthentication()
   .UseAuthorization()
   .UseFastEndpoints()
   .UseSwaggerGen();

app.MapHub<SymbolHub>("/hubs/symbols");

app.Run();