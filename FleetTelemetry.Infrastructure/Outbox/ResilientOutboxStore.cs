using FleetTelemetry.Application.Abstractions;
using FleetTelemetry.Application.Options;
using FleetTelemetry.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Outbox;

public sealed class ResilientOutboxStore : IOutboxSnapshotWriter
{
    private int _lastTargetWasFallback;
    public bool LastWriteUsedFallback => _lastTargetWasFallback == 1;
    
    private readonly IOutboxStore _primary;
    private readonly IOutboxStore _fallback;
    private readonly PersistenceOptions _options;
    private readonly ILogger<ResilientOutboxStore> _logger;

    public ResilientOutboxStore(
        IOutboxStore primary,
        IOutboxStore fallback,
        IOptions<PersistenceOptions> options,
        ILogger<ResilientOutboxStore> logger)
    {
        _primary = primary;
        _fallback = fallback;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SaveAsync(
    string deviceCode,
    IReadOnlyList<DeviceTelemetry> items,
    CancellationToken ct)
    {
        if (items.Count == 0)
        {
            await ClearBothAsync(deviceCode, ct);
            return;
        }

        if (_options.DatabaseEnabled)
        {
            try
            {
                await _primary.SaveAsync(deviceCode, items, ct);
                Interlocked.Exchange(ref _lastTargetWasFallback, 0);

                await SafeClearAsync(_fallback, deviceCode, ct);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "Cihaz {DeviceCode} için veritabanına yazılamadı, diske düşülüyor",
                    deviceCode);
            }
        }

        await _fallback.SaveAsync(deviceCode, items, ct);
        Interlocked.Exchange(ref _lastTargetWasFallback, 1);
    }

    private async Task ClearBothAsync(string deviceCode, CancellationToken ct)
    {
        if (_options.DatabaseEnabled)
        {
            await SafeClearAsync(_primary, deviceCode, ct);
        }

        await SafeClearAsync(_fallback, deviceCode, ct);
    }

    private async Task SafeClearAsync(IOutboxStore store, string deviceCode, CancellationToken ct)
    {
        try
        {
            await store.ClearAsync(deviceCode, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Cihaz {DeviceCode} için depo temizliği başarısız",
                deviceCode);
        }
    }

    public async Task<IReadOnlyList<DeviceTelemetry>> LoadMergedAsync(
        string deviceCode,
        CancellationToken ct)
    {
        var primary = await SafeLoadAsync(_primary, deviceCode, ct);
        var fallback = await SafeLoadAsync(_fallback, deviceCode, ct);

        if (primary.Count == 0)
        {
            return fallback;
        }

        if (fallback.Count == 0)
        {
            return primary;
        }

        return primary
            .Concat(fallback)
            .DistinctBy(x => x.DataDate)
            .OrderBy(x => x.DataDate)
            .ToArray();
    }

    public async Task<IReadOnlyCollection<string>> ListAllDeviceCodesAsync(
        CancellationToken ct)
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var code in await SafeListAsync(_primary, ct))
        {
            codes.Add(code);
        }

        foreach (var code in await SafeListAsync(_fallback, ct))
        {
            codes.Add(code);
        }

        return codes;
    }

    private async Task<IReadOnlyList<DeviceTelemetry>> SafeLoadAsync(
        IOutboxStore store,
        string deviceCode,
        CancellationToken ct)
    {
        try
        {
            return await store.LoadAsync(deviceCode, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Cihaz {DeviceCode} için depodan okuma başarısız",
                deviceCode);

            return [];
        }
    }

    private async Task<IReadOnlyCollection<string>> SafeListAsync(
        IOutboxStore store,
        CancellationToken ct)
    {
        try
        {
            return await store.ListDeviceCodesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Depo cihaz listesi okunamadı");
            return [];
        }
    }
}