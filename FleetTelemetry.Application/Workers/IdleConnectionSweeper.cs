using FleetTelemetry.Application.Abstractions;
using FleetTelemetry.Application.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Application.Workers;

public sealed class IdleConnectionSweeper : BackgroundService
{
    private readonly IDeviceConnectionRegistry _registry;
    private readonly IOutboxQueue _queue;
    private readonly IdleOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<IdleConnectionSweeper> _logger;

    public IdleConnectionSweeper(
        IDeviceConnectionRegistry registry,
        IOutboxQueue queue,
        IOptions<IdleOptions> options,
        TimeProvider timeProvider,
        ILogger<IdleConnectionSweeper> logger)
    {
        _registry = registry;
        _queue = queue;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.SweepInterval, _timeProvider);

        while (await SafeWaitAsync(timer, stoppingToken))
        {
            await SweepAsync(stoppingToken);
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        try
        {
            var threshold = _timeProvider.GetUtcNow() - _options.IdleThreshold;
            var removed = 0;

            foreach (var connection in _registry.Snapshot())
            {
                ct.ThrowIfCancellationRequested();

                if (connection.LastActivityAt > threshold)
                {
                    continue;
                }

                if (_queue.Count(connection.DeviceCode) > 0)
                {
                    _logger.LogDebug(
                        "Cihaz {DeviceCode} atıl ancak kuyruğunda veri var, silinmedi",
                        connection.DeviceCode);

                    continue;
                }

                if (await _registry.RemoveAsync(connection.DeviceCode))
                {
                    removed++;
                }
            }

            if (removed > 0)
            {
                _logger.LogInformation(
                    "Atıl temizliği: {Removed} bağlantı kapatıldı, {Remaining} bağlantı kaldı",
                    removed,
                    _registry.Count);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Atıl temizliği sırasında hata oluştu");
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}