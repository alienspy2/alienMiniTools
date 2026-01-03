using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MiniTCPTunnel.Server;
using MiniTCPTunnel.Shared.Config;

var builder = Host.CreateApplicationBuilder(args);

var configPath = ConfigPathResolver.ResolveConfigPath(args, "server.json");

builder.Configuration.Sources.Clear();
builder.Configuration
    .AddJsonFile(configPath, optional: false, reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

builder.Services.Configure<ServerConfig>(builder.Configuration);
builder.Services.AddSingleton<ServerApp>();
builder.Services.AddHostedService<ServerHostedService>();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var host = builder.Build();
await host.RunAsync();
