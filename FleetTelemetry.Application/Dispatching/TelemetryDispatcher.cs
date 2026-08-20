using System.Collections.Concurrent;
using System.Diagnostics;
using FleetTelemetry.Application.Abstractions;
using FleetTelemetry.Application.Options;
using FleetTelemetry.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Application.Dispatching;

public sealed class TelemetryDispatcher : ITelemetryDispatcher
{
    private readonly ConcurrentDictionary<string, DeviceSendState> _states =
        new(StringComparer.Ordinal);

    private readonly ISendDecisionPolicy _policy;
    private readonly IOutboxQueue _queue;
    private readonly IDeviceConnectionRegistry _registry;
    private readonly PollingOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TelemetryDispatcher> _logger;

    public TelemetryDispatcher(
        ISendDecisionPolicy policy,
        IOutboxQueue queue,
        IDeviceConnectionRegistry registry,
        IOptions<PollingOptions> options,
        TimeProvider timeProvider,
        ILogger<TelemetryDispatcher> logger)
    {
        _policy = policy;
        _queue = queue;
        _registry = registry;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<DispatchSummary> DispatchAsync(
        IReadOnlyCollection<DeviceTelemetry> telemetries,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var counters = new Counters();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = _options.MaxDegreeOfParallelism,
            CancellationToken = ct
        };

        await Parallel.ForEachAsync(
            telemetries,
            parallelOptions,
            async (telemetry, token) =>
                await ProcessDeviceAsync(telemetry, counters, token));

        stopwatch.Stop();

        return new DispatchSummary
        {
            Total = telemetries.Count,
            Sent = counters.Sent,
            SkippedByPolicy = counters.SkippedByPolicy,
            SkippedBackoff = counters.SkippedBackoff,
            Queued = counters.Queued,
            Failed = counters.Failed,
            ElapsedMs = stopwatch.ElapsedMilliseconds
        };
    }

    private async Task ProcessDeviceAsync(
        DeviceTelemetry telemetry,
        Counters counters,
        CancellationToken ct)
    {
        try
        {
            _states.TryGetValue(telemetry.DeviceCode, out var previousState);

            var reason = _policy.Decide(telemetry, previousState);

            if (reason == SendReason.DoNotSend)
            {
                counters.IncrementSkippedByPolicy();
                TouchDataSeen(telemetry);
                return;
            }

            _queue.Enqueue(telemetry.DeviceCode, telemetry);

            var outcome = await FlushQueueAsync(telemetry.DeviceCode, ct);

            switch (outcome)
            {
                case SendOutcome.Sent:
                    counters.IncrementSent();
                    MarkSent(telemetry);
                    break;

                case SendOutcome.SkippedBackoff:
                    counters.IncrementSkippedBackoff();
                    TouchDataSeen(telemetry);
                    break;

                default:
                    counters.IncrementFailed();
                    TouchDataSeen(telemetry);
                    break;
            }

            counters.AddQueued(_queue.Count(telemetry.DeviceCode));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            counters.IncrementFailed();

            _logger.LogError(
                ex,
                "Cihaz {DeviceCode} işlenirken beklenmeyen hata",
                telemetry.DeviceCode);
        }
    }

    private async Task<SendOutcome> FlushQueueAsync(string deviceCode, CancellationToken ct)
    {
        var connection = _registry.GetOrCreate(deviceCode);
        var lastOutcome = SendOutcome.Sent;

        while (_queue.TryDequeue(deviceCode, out var item))
        {
            lastOutcome = await connection.SendAsync(item, ct);

            if (lastOutcome != SendOutcome.Sent)
            {
                _queue.Requeue(deviceCode, item);
                break;
            }
        }

        return lastOutcome;
    }

    private void MarkSent(DeviceTelemetry telemetry)
    {
        var now = _timeProvider.GetUtcNow();

        _states.AddOrUpdate(
            telemetry.DeviceCode,
            _ => new DeviceSendState
            {
                LastSentAt = now,
                LastKnownContact = telemetry.IsOnline,
                LastDataSeenAt = now
            },
            (_, existing) => existing.WithSent(now, telemetry.IsOnline));
    }

    private void TouchDataSeen(DeviceTelemetry telemetry)
    {
        var now = _timeProvider.GetUtcNow();

        _states.AddOrUpdate(
            telemetry.DeviceCode,
            _ => new DeviceSendState
            {
                LastSentAt = DateTimeOffset.MinValue,
                LastKnownContact = telemetry.IsOnline,
                LastDataSeenAt = now
            },
            (_, existing) => existing.WithDataSeen(now));
    }

    private sealed class Counters
    {
        private int _sent;
        private int _skippedByPolicy;
        private int _skippedBackoff;
        private int _failed;
        private int _queued;

        public int Sent => _sent;
        public int SkippedByPolicy => _skippedByPolicy;
        public int SkippedBackoff => _skippedBackoff;
        public int Failed => _failed;
        public int Queued => _queued;

        public void IncrementSent() => Interlocked.Increment(ref _sent);
        public void IncrementSkippedByPolicy() => Interlocked.Increment(ref _skippedByPolicy);
        public void IncrementSkippedBackoff() => Interlocked.Increment(ref _skippedBackoff);
        public void IncrementFailed() => Interlocked.Increment(ref _failed);
        public void AddQueued(int count) => Interlocked.Add(ref _queued, count);
    }
}