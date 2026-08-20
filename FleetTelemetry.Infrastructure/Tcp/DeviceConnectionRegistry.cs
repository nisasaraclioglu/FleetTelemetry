using System.Collections.Concurrent;
using FleetTelemetry.Application.Abstractions;
using FleetTelemetry.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Tcp;

public sealed class DeviceConnectionRegistry : IDeviceConnectionRegistry
{
    private readonly ConcurrentDictionary<string, IDeviceConnection> _connections =
        new(StringComparer.Ordinal);

    private readonly ITcpConnectionFactory _connectionFactory;
    private readonly ITelemetryEncoder _encoder;
    private readonly IReconnectPolicy _reconnectPolicy;
    private readonly TcpOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<DeviceConnectionRegistry> _logger;

    private bool _disposed;

    public DeviceConnectionRegistry(
        ITcpConnectionFactory connectionFactory,
        ITelemetryEncoder encoder,
        IReconnectPolicy reconnectPolicy,
        IOptions<TcpOptions> options,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory)
    {
        _connectionFactory = connectionFactory;
        _encoder = encoder;
        _reconnectPolicy = reconnectPolicy;
        _options = options.Value;
        _timeProvider = timeProvider;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<DeviceConnectionRegistry>();
    }

    public int Count => _connections.Count;

    public IDeviceConnection GetOrCreate(string deviceCode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _connections.GetOrAdd(deviceCode, CreateConnection);
    }

    public bool TryGet(string deviceCode, out IDeviceConnection connection) =>
        _connections.TryGetValue(deviceCode, out connection!);

    public async Task<bool> RemoveAsync(string deviceCode)
    {
        if (!_connections.TryRemove(deviceCode, out var connection))
        {
            return false;
        }

        await SafeDisposeAsync(connection);

        _logger.LogInformation(
            "Cihaz {DeviceCode} bağlantısı kapatıldı ve kayıttan silindi",
            deviceCode);

        return true;
    }

    public IReadOnlyCollection<IDeviceConnection> Snapshot() =>
        _connections.Values.ToArray();

    private IDeviceConnection CreateConnection(string deviceCode)
    {
        _logger.LogDebug("Cihaz {DeviceCode} için bağlantı nesnesi oluşturuldu", deviceCode);

        return new TcpDeviceConnection(
            deviceCode,
            _connectionFactory,
            _encoder,
            _reconnectPolicy,
            _options,
            _timeProvider,
            _loggerFactory.CreateLogger<TcpDeviceConnection>());
    }

    private async ValueTask SafeDisposeAsync(IDeviceConnection connection)
    {
        try
        {
            await connection.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Cihaz {DeviceCode} bağlantısı kapatılırken hata oluştu",
                connection.DeviceCode);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        var connections = _connections.Values.ToArray();
        _connections.Clear();

        foreach (var connection in connections)
        {
            await SafeDisposeAsync(connection);
        }

        _logger.LogInformation(
            "{Count} bağlantı kapatıldı",
            connections.Length);
    }
}