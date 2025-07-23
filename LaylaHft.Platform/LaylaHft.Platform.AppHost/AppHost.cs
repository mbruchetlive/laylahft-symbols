var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.LaylaHft_Platform_MarketData>("laylahft-platform-marketdata");

builder.Build().Run();
