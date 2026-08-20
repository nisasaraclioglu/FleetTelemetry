using FleetTelemetry.Domain;

namespace FleetTelemetry.Application.Abstractions;

public interface ITelemetrySource
{
    Task<IReadOnlyCollection<DeviceTelemetry>> GetLatestAsync(CancellationToken ct);
}