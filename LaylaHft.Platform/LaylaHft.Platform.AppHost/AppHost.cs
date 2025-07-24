using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.LaylaHft_Platform_MarketData>("laylahft-platform-marketdata")
    .WithHttpsEndpoint(name:"laylahft-platform-marketdata-https") ;

builder.Build().Run();
