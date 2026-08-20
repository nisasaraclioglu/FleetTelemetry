using FleetTelemetry.Application.Abstractions;
using FleetTelemetry.Application.Options;
using FleetTelemetry.Domain;
using Microsoft.Extensions.Logging;

namespace FleetTelemetry.Infrastructure.Tcp;

public sealed class TcpDeviceConnection : IDeviceConnection
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly ITcpConnectionFactory _factory;
    private readonly ITelemetryEncoder _encoder;
    private readonly IReconnectPolicy _reconnectPolicy;
    private readonly TcpOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;

    private ITcpConnection? _connection;
    private ConnectionState _state = ConnectionState.Disconnected;
    private int _failedAttempts;
    private DateTimeOffset _retryNotBefore = DateTimeOffset.MinValue;
    private DateTimeOffset _lastActivityAt;

    public TcpDeviceConnection(
        string deviceCode,
        ITcpConnectionFactory factory,
        ITelemetryEncoder encoder,
        IReconnectPolicy reconnectPolicy,
        TcpOptions options,
        TimeProvider timeProvider,
        ILogger logger)
    {
        DeviceCode = deviceCode;
        _factory = factory;
        _encoder = encoder;
        _reconnectPolicy = reconnectPolicy;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
        _lastActivityAt = timeProvider.GetUtcNow();
    }

    public string DeviceCode { get; }

    public ConnectionState State => _state;

    public DateTimeOffset LastActivityAt => _lastActivityAt;

    public async Task<SendOutcome> SendAsync(DeviceTelemetry telemetry, CancellationToken ct)
    {
        if (_state == ConnectionState.Disposed)
        {
            return SendOutcome.Disposed;
        }

        var now = _timeProvider.GetUtcNow();

        if (_state == ConnectionState.Faulted && now < _retryNotBefore)
        {
            return SendOutcome.SkippedBackoff;
        }

        await _gate.WaitAsync(ct);

        try
        {
            if (_state == ConnectionState.Disposed)
            {
                return SendOutcome.Disposed;
            }

            if (_connection is null && !await TryConnectAsync(ct))
            {
                return SendOutcome.ConnectFailed;
            }

            return await TrySendAsync(telemetry, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> TryConnectAsync(CancellationToken ct)
    {
        _state = ConnectionState.Connecting;

        var connection = _factory.Create();

        try
        {
            using var timeout = CreateTimeout(_options.ConnectTimeout, ct);

            await connection.ConnectAsync(_options.Host, _options.Port, timeout.Token);

            _connection = connection;
            _state = ConnectionState.Connected;

            _logger.LogInformation(
                "Cihaz {DeviceCode} için bağlantı kuruldu ({Attempt}. denemede)",
                DeviceCode,
                _failedAttempts + 1);

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException
                                   || ct.IsCancellationRequested is false)
        {
            await SafeDisposeAsync(connection);
            RegisterFailure("bağlanma", ex);
            return false;
        }
    }

    private async Task<SendOutcome> TrySendAsync(DeviceTelemetry telemetry, CancellationToken ct)
    {
        try
        {
            var payload = _encoder.Encode(telemetry);

            using var timeout = CreateTimeout(_options.SendTimeout, ct);

            await _connection!.SendAsync(payload, timeout.Token);

            _failedAttempts = 0;
            _retryNotBefore = DateTimeOffset.MinValue;
            _lastActivityAt = _timeProvider.GetUtcNow();
            _state = ConnectionState.Connected;

            return SendOutcome.Sent;
        }
        catch (Exception ex) when (ex is not OperationCanceledException
                                   || ct.IsCancellationRequested is false)
        {
            await CloseConnectionAsync();
            RegisterFailure("gönderim", ex);
            return SendOutcome.SendFailed;
        }
    }

    private void RegisterFailure(string phase, Exception ex)
    {
        _failedAttempts++;
        _state = ConnectionState.Faulted;

        var delay = _reconnectPolicy.GetDelay(_failedAttempts);
        _retryNotBefore = _timeProvider.GetUtcNow() + delay;

        var level = _failedAttempts == 1 || _failedAttempts % 10 == 0
            ? LogLevel.Warning
            : LogLevel.Debug;

        _logger.Log(
            level,
            ex,
            "Cihaz {DeviceCode} {Phase} hatası ({Attempt}. deneme), {Delay} sonra tekrar denenecek",
            DeviceCode,
            phase,
            _failedAttempts,
            delay);
    }

    private CancellationTokenSource CreateTimeout(TimeSpan timeout, CancellationToken ct)
    {
        var source = new CancellationTokenSource(timeout, _timeProvider);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, source.Token);

        linked.Token.Register(source.Dispose);

        return linked;
    }

    private async Task CloseConnectionAsync()
    {
        if (_connection is not null)
        {
            await SafeDisposeAsync(_connection);
            _connection = null;
        }
    }

    private async ValueTask SafeDisposeAsync(ITcpConnection connection)
    {
        try
        {
            await connection.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Cihaz {DeviceCode} bağlantısı kapatılırken hata oluştu",
                DeviceCode);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_state == ConnectionState.Disposed)
        {
            return;
        }

        await _gate.WaitAsync();

        try
        {
            await CloseConnectionAsync();
            _state = ConnectionState.Disposed;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}