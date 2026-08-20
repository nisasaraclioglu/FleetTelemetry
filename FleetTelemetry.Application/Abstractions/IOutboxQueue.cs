using FleetTelemetry.Domain;

namespace FleetTelemetry.Application.Abstractions;

public interface IOutboxQueue
{
    OutboxEnqueueResult Enqueue(string deviceCode, DeviceTelemetry telemetry);

    bool TryDequeue(string deviceCode, out DeviceTelemetry telemetry);

    void Requeue(string deviceCode, DeviceTelemetry telemetry);

    void Load(string deviceCode, IEnumerable<DeviceTelemetry> items);

    IReadOnlyList<DeviceTelemetry> Snapshot(string deviceCode);

    int Count(string deviceCode);

    IReadOnlyCollection<string> DeviceCodes { get; }

    long TotalEvicted { get; }
}