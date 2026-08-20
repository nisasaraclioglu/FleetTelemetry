using FleetTelemetry.Domain;

namespace FleetTelemetry.Application.Abstractions;

public interface IDeviceConnection : IAsyncDisposable
{
    string DeviceCode { get; }

    ConnectionState State { get; }

    DateTimeOffset LastActivityAt { get; }

    Task<SendOutcome> SendAsync(DeviceTelemetry telemetry, CancellationToken ct);
}