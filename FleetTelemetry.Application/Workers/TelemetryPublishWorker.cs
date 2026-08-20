using FleetTelemetry.Application.Abstractions;
using FleetTelemetry.Application.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Application.Workers;

public sealed class TelemetryPublishWorker : BackgroundService
{
    private readonly ITelemetrySource _source;
    private readonly ITelemetryDispatcher _dispatcher;
    private readonly IOutboxRecovery _recovery;
    private readonly PollingOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TelemetryPublishWorker> _logger;

    private int _consecutiveOverruns;

    public TelemetryPublishWorker(
        ITelemetrySource source,
        ITelemetryDispatcher dispatcher,
        IOutboxRecovery recovery,
        IOptions<PollingOptions> options,
        TimeProvider timeProvider,
        ILogger<TelemetryPublishWorker> logger)
    {
        _source = source;
        _dispatcher = dispatcher;
        _recovery = recovery;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RestoreQueueAsync(stoppingToken);

        _logger.LogInformation(
            "Telemetri turu başlatıldı: aralık={Interval} paralellik={Parallelism}",
            _options.Interval,
            _options.MaxDegreeOfParallelism);

        using var timer = new PeriodicTimer(_options.Interval, _timeProvider);

        while (await SafeWaitAsync(timer, stoppingToken))
        {
            await RunTickAsync(stoppingToken);
        }

        _logger.LogInformation("Telemetri turu durduruldu");
    }

    private async Task RestoreQueueAsync(CancellationToken ct)
    {
        try
        {
            await _recovery.RestoreAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(
                ex,
                "Kuyruk geri yüklenemedi, boş kuyrukla devam ediliyor");
        }
    }

    private async Task RunTickAsync(CancellationToken ct)
    {
        try
        {
            using var timeout = new CancellationTokenSource(
                _options.SourceTimeout,
                _timeProvider);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                ct,
                timeout.Token);

            var telemetries = await _source.GetLatestAsync(linked.Token);

            if (telemetries.Count == 0)
            {
                _logger.LogDebug("Kaynak veri döndürmedi, tur atlandı");
                return;
            }

            var summary = await _dispatcher.DispatchAsync(telemetries, ct);

            _logger.LogInformation(
                "Tur: toplam={Total} gönderildi={Sent} politika={Policy} " +
                "backoff={Backoff} kuyrukta={Queued} hata={Failed} süre={ElapsedMs}ms",
                summary.Total,
                summary.Sent,
                summary.SkippedByPolicy,
                summary.SkippedBackoff,
                summary.Queued,
                summary.Failed,
                summary.ElapsedMs);

            CheckOverrun(summary.ElapsedMs);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tur sırasında beklenmeyen hata, döngü sürdürülüyor");
        }
    }

    private void CheckOverrun(long elapsedMs)
    {
        if (elapsedMs <= _options.Interval.TotalMilliseconds)
        {
            _consecutiveOverruns = 0;
            return;
        }

        _consecutiveOverruns++;

        var level = _consecutiveOverruns >= 3 ? LogLevel.Error : LogLevel.Warning;

        _logger.Log(
            level,
            "Tur süresi aşıldı: {ElapsedMs}ms > {IntervalMs}ms ({Count}. kez üst üste)",
            elapsedMs,
            _options.Interval.TotalMilliseconds,
            _consecutiveOverruns);
    }

    private static async Task<bool> SafeWaitAsync(
        PeriodicTimer timer,
        CancellationToken ct)
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