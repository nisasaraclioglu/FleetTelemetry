using System.Diagnostics;
using FleetTelemetry.Application.Abstractions;
using FleetTelemetry.Application.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Application.Workers;

public sealed class OutboxSnapshotWorker : BackgroundService
{
    private readonly IOutboxQueue _queue;
    private readonly IOutboxSnapshotWriter _writer;
    private readonly PersistenceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OutboxSnapshotWorker> _logger;

    public OutboxSnapshotWorker(
        IOutboxQueue queue,
        IOutboxSnapshotWriter writer,
        IOptions<PersistenceOptions> options,
        TimeProvider timeProvider,
        ILogger<OutboxSnapshotWorker> logger)
    {
        _queue = queue;
        _writer = writer;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.SnapshotInterval, _timeProvider);

        while (await SafeWaitAsync(timer, stoppingToken))
        {
            await RunSnapshotAsync(stoppingToken);
        }

        if (_options.SnapshotOnShutdown)
        {
            await RunFinalSnapshotAsync();
        }
    }

    private async Task RunSnapshotAsync(CancellationToken ct)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var deviceCount = 0;
            var recordCount = 0;

            foreach (var deviceCode in _queue.DeviceCodes)
            {
                ct.ThrowIfCancellationRequested();

                var items = _queue.Snapshot(deviceCode);

                await _writer.SaveAsync(deviceCode, items, ct);

                if (items.Count > 0)
                {
                    deviceCount++;
                    recordCount += items.Count;
                }
            }

            stopwatch.Stop();

            if (recordCount > 0)
            {
                _logger.LogInformation(
                    "Snapshot: cihaz={DeviceCount} kayit={RecordCount} hedef={Target} süre={ElapsedMs}ms",
                    deviceCount,
                    recordCount,
                    _writer.LastWriteUsedFallback ? "Disk" : "Veritabani",
                    stopwatch.ElapsedMilliseconds);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Snapshot alınırken hata oluştu, sonraki turda denenecek");
        }
    }

    private async Task RunFinalSnapshotAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var recordCount = 0;

            foreach (var deviceCode in _queue.DeviceCodes)
            {
                var items = _queue.Snapshot(deviceCode);
                await _writer.SaveAsync(deviceCode, items, timeout.Token);
                recordCount += items.Count;
            }

            _logger.LogInformation(
                "Kapanış snapshot'ı alındı: kayıt={RecordCount}",
                recordCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kapanış snapshot'ı alınamadı");
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