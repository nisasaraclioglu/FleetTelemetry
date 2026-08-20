using FleetTelemetry.Domain;

namespace FleetTelemetry.Application.Abstractions;

public interface ITelemetryDispatcher
{
    Task<DispatchSummary> DispatchAsync(
        IReadOnlyCollection<DeviceTelemetry> telemetries,
        CancellationToken ct);
}