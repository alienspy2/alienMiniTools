using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MiniTCPTunnel.Client;
using MiniTCPTunnel.Shared.Config;

var builder = Host.CreateApplicationBuilder(args);

var configPath = ConfigPathResolver.ResolveConfigPath(args, "client.json");

builder.Configuration.Sources.Clear();
builder.Configuration
    .AddJsonFile(configPath, optional: false, reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

builder.Services.Configure<ClientConfig>(builder.Configuration);
builder.Services.AddSingleton<ClientApp>();
builder.Services.AddHostedService<ClientHostedService>();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var host = builder.Build();
await host.RunAsync();
