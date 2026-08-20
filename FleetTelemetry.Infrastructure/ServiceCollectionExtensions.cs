using FleetTelemetry.Application.Abstractions;
using FleetTelemetry.Application.Dispatching;
using FleetTelemetry.Application.Options;
using FleetTelemetry.Application.Policies;
using FleetTelemetry.Application.Workers;
using FleetTelemetry.Infrastructure.Http;
using FleetTelemetry.Infrastructure.Outbox;
using FleetTelemetry.Infrastructure.Resilience;
using FleetTelemetry.Infrastructure.Tcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FleetTelemetry.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFleetTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptionsSection<PollingOptions>(configuration, PollingOptions.SectionName);
        services.AddOptionsSection<TcpOptions>(configuration, TcpOptions.SectionName);
        services.AddOptionsSection<ReconnectOptions>(configuration, ReconnectOptions.SectionName);
        services.AddOptionsSection<OutboxOptions>(configuration, OutboxOptions.SectionName);
        services.AddOptionsSection<PersistenceOptions>(configuration, PersistenceOptions.SectionName);
        services.AddOptionsSection<SendPolicyOptions>(configuration, SendPolicyOptions.SectionName);
        services.AddOptionsSection<IdleOptions>(configuration, IdleOptions.SectionName);
        services.AddOptionsSection<TimeZoneOptions>(configuration, TimeZoneOptions.SectionName);

        services.TryAddSingletonTimeProvider();

        services.AddSingleton<ISendDecisionPolicy, ContactBasedSendPolicy>();
        services.AddSingleton<IReconnectPolicy, ReconnectPolicy>();
        services.AddSingleton<ITelemetryEncoder, PlaceholderJsonEncoder>();
        services.AddSingleton<IOutboxQueue, BoundedOutboxQueue>();

        services.AddSingleton<ITcpConnectionFactory, SocketTcpConnectionFactory>();
        services.AddSingleton<IDeviceConnectionRegistry, DeviceConnectionRegistry>();

        services.AddSingleton<FileOutboxStore>();
        services.AddSingleton<NullOutboxStore>();

        services.AddSingleton<ResilientOutboxStore>(provider => new ResilientOutboxStore(
            provider.GetRequiredService<NullOutboxStore>(),
            provider.GetRequiredService<FileOutboxStore>(),
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PersistenceOptions>>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ResilientOutboxStore>>()));

        services.AddSingleton<IOutboxSnapshotWriter>(provider =>
            provider.GetRequiredService<ResilientOutboxStore>());

        services.AddSingleton<IOutboxRecovery, OutboxRecoveryService>();

        services.AddSingleton<ITelemetrySource, FakeTelemetrySource>();
        services.AddSingleton<ITelemetryDispatcher, TelemetryDispatcher>();

        services.AddHostedService<TelemetryPublishWorker>();
        services.AddHostedService<OutboxSnapshotWorker>();
        services.AddHostedService<IdleConnectionSweeper>();

        return services;
    }

    private static void AddOptionsSection<T>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName)
        where T : class
    {
        services
            .AddOptions<T>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateOnStart();
    }

    private static void TryAddSingletonTimeProvider(this IServiceCollection services)
    {
        if (services.Any(x => x.ServiceType == typeof(TimeProvider)))
        {
            return;
        }

        services.AddSingleton(TimeProvider.System);
    }
}