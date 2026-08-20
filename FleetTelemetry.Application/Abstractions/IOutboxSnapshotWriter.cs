using FleetTelemetry.Domain;

namespace FleetTelemetry.Application.Abstractions;

public interface IOutboxSnapshotWriter
{
    bool LastWriteUsedFallback { get; }

    Task SaveAsync(string deviceCode, IReadOnlyList<DeviceTelemetry> items, CancellationToken ct);
}