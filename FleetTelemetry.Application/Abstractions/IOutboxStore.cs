using FleetTelemetry.Domain;

namespace FleetTelemetry.Application.Abstractions;

public interface IOutboxStore
{
    Task SaveAsync(
        string deviceCode,
        IReadOnlyList<DeviceTelemetry> items,
        CancellationToken ct);

    Task<IReadOnlyList<DeviceTelemetry>> LoadAsync(
        string deviceCode,
        CancellationToken ct);

    Task<IReadOnlyCollection<string>> ListDeviceCodesAsync(CancellationToken ct);

    Task ClearAsync(string deviceCode, CancellationToken ct);
}