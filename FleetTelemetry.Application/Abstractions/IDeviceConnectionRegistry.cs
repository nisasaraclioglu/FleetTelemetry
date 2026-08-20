namespace FleetTelemetry.Application.Abstractions;

public interface IDeviceConnectionRegistry : IAsyncDisposable
{
    IDeviceConnection GetOrCreate(string deviceCode);

    bool TryGet(string deviceCode, out IDeviceConnection connection);

    Task<bool> RemoveAsync(string deviceCode);

    IReadOnlyCollection<IDeviceConnection> Snapshot();

    int Count { get; }
}