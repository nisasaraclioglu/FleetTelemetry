using FleetTelemetry.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace FleetTelemetry.Infrastructure.Outbox;

public sealed class OutboxRecoveryService : IOutboxRecovery
{
    private readonly ResilientOutboxStore _store;
    private readonly IOutboxQueue _queue;
    private readonly ILogger<OutboxRecoveryService> _logger;

    public OutboxRecoveryService(
        ResilientOutboxStore store,
        IOutboxQueue queue,
        ILogger<OutboxRecoveryService> logger)
    {
        _store = store;
        _queue = queue;
        _logger = logger;
    }

    public async Task<int> RestoreAsync(CancellationToken ct)
    {
        var deviceCodes = await _store.ListAllDeviceCodesAsync(ct);

        if (deviceCodes.Count == 0)
        {
            _logger.LogInformation("Geri yüklenecek kuyruk verisi bulunamadı");
            return 0;
        }

        var totalRecords = 0;

        foreach (var deviceCode in deviceCodes)
        {
            ct.ThrowIfCancellationRequested();

            var items = await _store.LoadMergedAsync(deviceCode, ct);

            if (items.Count == 0)
            {
                continue;
            }

            _queue.Load(deviceCode, items);
            totalRecords += _queue.Count(deviceCode);
        }

        _logger.LogInformation(
            "Kuyruk geri yüklendi: cihaz={DeviceCount} kayıt={RecordCount}",
            deviceCodes.Count,
            totalRecords);

        return totalRecords;
    }
}