
using FastEndpoints;

namespace LaylaHft.Platform.MarketData
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.AddServiceDefaults();

            builder.Services.AddFastEndpoints();

            // Add services to the container.

            builder.Services.AddControllers();
            // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
            builder.Services.AddOpenApi();

            builder.Services.AddBinance();
            builder.Services.AddSingleton<Services.ISymbolStore, Services.SymbolStore>();
            builder.Services.AddSingleton<Services.SymbolDownloader>();
            
            builder.Services.AddHostedService<SymbolDownloaderBackgroundService>();

            var app = builder.Build();

            app.MapDefaultEndpoints();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();


            app.MapControllers();

            app.UseFastEndpoints();
            app.Run();
        }
    }
}
