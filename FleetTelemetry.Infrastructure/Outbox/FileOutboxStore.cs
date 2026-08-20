using System.Collections.Concurrent;
using System.Text.Json;
using FleetTelemetry.Application.Abstractions;
using FleetTelemetry.Application.Options;
using FleetTelemetry.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Outbox;

public sealed class FileOutboxStore : IOutboxStore
{
    private const string DataExtension = ".json";
    private const string TempExtension = ".tmp";
    private const string CorruptExtension = ".corrupt";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks =
        new(StringComparer.Ordinal);

    private readonly string _directory;
    private readonly ILogger<FileOutboxStore> _logger;

    public FileOutboxStore(
    IOptions<PersistenceOptions> options,
    ILogger<FileOutboxStore> logger)
    {
        _directory = Path.GetFullPath(options.Value.FileFallbackDirectory);
        _logger = logger;

        Directory.CreateDirectory(_directory);

        _logger.LogInformation("Outbox dizini: {Directory}", _directory);
    }

    public async Task SaveAsync(
        string deviceCode,
        IReadOnlyList<DeviceTelemetry> items,
        CancellationToken ct)
    {
        if (items.Count == 0)
        {
            await ClearAsync(deviceCode, ct);
            return;
        }

        var gate = GetLock(deviceCode);
        await gate.WaitAsync(ct);

        try
        {
            var target = GetPath(deviceCode, DataExtension);
            var temp = GetPath(deviceCode, TempExtension);

            await using (var stream = File.Create(temp))
            {
                await JsonSerializer.SerializeAsync(stream, items, JsonOptions, ct);
                await stream.FlushAsync(ct);
            }

            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<DeviceTelemetry>> LoadAsync(
        string deviceCode,
        CancellationToken ct)
    {
        var gate = GetLock(deviceCode);
        await gate.WaitAsync(ct);

        try
        {
            var path = GetPath(deviceCode, DataExtension);

            if (!File.Exists(path))
            {
                return [];
            }

            try
            {
                await using var stream = File.OpenRead(path);

                var items = await JsonSerializer
                    .DeserializeAsync<List<DeviceTelemetry>>(stream, JsonOptions, ct);

                return items ?? [];
            }
            catch (JsonException ex)
            {
                Quarantine(path, deviceCode, ex);
                return [];
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<IReadOnlyCollection<string>> ListDeviceCodesAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var codes = Directory
            .EnumerateFiles(_directory, $"*{DataExtension}")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .ToArray();

        return Task.FromResult<IReadOnlyCollection<string>>(codes);
    }

    public async Task ClearAsync(string deviceCode, CancellationToken ct)
    {
        var gate = GetLock(deviceCode);
        await gate.WaitAsync(ct);

        try
        {
            DeleteIfExists(GetPath(deviceCode, DataExtension));
            DeleteIfExists(GetPath(deviceCode, TempExtension));
        }
        finally
        {
            gate.Release();
        }
    }

    private SemaphoreSlim GetLock(string deviceCode) =>
        _locks.GetOrAdd(deviceCode, static _ => new SemaphoreSlim(1, 1));

    private string GetPath(string deviceCode, string extension) =>
        Path.Combine(_directory, MakeSafeFileName(deviceCode) + extension);

    private static string MakeSafeFileName(string deviceCode)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var buffer = deviceCode.ToCharArray();

        for (var i = 0; i < buffer.Length; i++)
        {
            if (Array.IndexOf(invalid, buffer[i]) >= 0)
            {
                buffer[i] = '_';
            }
        }

        return new string(buffer);
    }

    private void Quarantine(string path, string deviceCode, Exception ex)
    {
        try
        {
            var target = path + CorruptExtension;
            File.Move(path, target, overwrite: true);

            _logger.LogError(
                ex,
                "Cihaz {DeviceCode} için bozuk dosya karantinaya alındı: {Path}",
                deviceCode,
                target);
        }
        catch (IOException moveError)
        {
            _logger.LogError(
                moveError,
                "Cihaz {DeviceCode} için bozuk dosya taşınamadı: {Path}",
                deviceCode,
                path);
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}