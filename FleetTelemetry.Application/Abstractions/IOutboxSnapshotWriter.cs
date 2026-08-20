using FleetTelemetry.Domain;

namespace FleetTelemetry.Application.Abstractions;

public interface IOutboxSnapshotWriter
{
    Task SaveAsync(
        string deviceCode,
        IReadOnlyList<DeviceTelemetry> items,
        CancellationToken ct);
}