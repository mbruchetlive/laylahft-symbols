
using FastEndpoints;
using FastEndpoints.Security;

namespace LaylaHft.Platform.MarketData
{
    public class Program
    {
        public static void Main(string[] args)
        {
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
               .AddFastEndpoints();

            // Add services to the container.

            builder.Services.AddControllers();
            // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
            builder.Services.AddOpenApi();

            builder.Services.AddBinance();
            builder.Services.AddSingleton<Services.ISymbolStore, Services.SymbolStore>();
            builder.Services.AddSingleton<Services.SymbolDownloader>();
            builder.Services.AddSingleton<IMyAuthService, MyAuthService>();

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

            app.UseAuthentication()
               .UseAuthorization()
               .UseFastEndpoints();

            app.Run();
        }
    }
}
