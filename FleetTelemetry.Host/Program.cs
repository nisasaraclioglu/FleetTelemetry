using FleetTelemetry.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddFleetTelemetry(builder.Configuration);

var host = builder.Build();

await host.RunAsync();