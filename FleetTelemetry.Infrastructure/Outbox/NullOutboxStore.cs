using FleetTelemetry.Application.Abstractions;
using FleetTelemetry.Domain;

namespace FleetTelemetry.Infrastructure.Outbox;

/// <summary>
/// Veritabanı deposu için geçici yer tutucu.
/// Veritabanı türü ve bağlantı bilgisi sağlanmadığı için boş bırakılmıştır.
/// Bilgi geldiğinde bu sınıfın yerine DatabaseOutboxStore yazılacaktır.
/// </summary>
public sealed class NullOutboxStore : IOutboxStore
{
    public Task SaveAsync(
        string deviceCode,
        IReadOnlyList<DeviceTelemetry> items,
        CancellationToken ct) => Task.CompletedTask;

    public Task<IReadOnlyList<DeviceTelemetry>> LoadAsync(
        string deviceCode,
        CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DeviceTelemetry>>([]);

    public Task<IReadOnlyCollection<string>> ListDeviceCodesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyCollection<string>>([]);

    public Task ClearAsync(string deviceCode, CancellationToken ct) => Task.CompletedTask;
}