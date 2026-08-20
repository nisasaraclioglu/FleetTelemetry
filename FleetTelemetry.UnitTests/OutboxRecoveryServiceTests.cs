using FleetTelemetry.Application.Options;
using FleetTelemetry.Domain;
using FleetTelemetry.Infrastructure.Outbox;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.UnitTests;

public sealed class OutboxRecoveryServiceTests : IDisposable
{
    private static readonly DateTimeOffset Base =
        new(2026, 1, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly string _directory;
    private readonly FileOutboxStore _fileStore;

    public OutboxRecoveryServiceTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "fleet-telemetry-recovery-tests",
            Guid.NewGuid().ToString("N"));

        _fileStore = new FileOutboxStore(
            Options.Create(new PersistenceOptions { FileFallbackDirectory = _directory }),
            NullLogger<FileOutboxStore>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static DeviceTelemetry Telemetry(string deviceCode, int secondsOffset) => new()
    {
        DeviceCode = deviceCode,
        DataDate = Base.AddSeconds(secondsOffset),
        GpsLat = 40.9,
        GpsLon = 38.3,
        Speed = 60,
        Angle = 90,
        IsOnline = true
    };

    private static BoundedOutboxQueue CreateQueue(int capacity = 100) =>
        new(Options.Create(new OutboxOptions { MaxItemsPerDevice = capacity }));

    private ResilientOutboxStore CreateStore(bool databaseEnabled = false) =>
        new(
            new NullOutboxStore(),
            _fileStore,
            Options.Create(new PersistenceOptions { DatabaseEnabled = databaseEnabled }),
            NullLogger<ResilientOutboxStore>.Instance);

    private OutboxRecoveryService CreateSut(BoundedOutboxQueue queue) =>
        new(CreateStore(), queue, NullLogger<OutboxRecoveryService>.Instance);

    [Fact]
    public async Task DepoBossa_HicbirSeyYuklenmez()
    {
        var queue = CreateQueue();
        var sut = CreateSut(queue);

        var count = await sut.RestoreAsync(default);

        Assert.Equal(0, count);
        Assert.Empty(queue.DeviceCodes);
    }

    [Fact]
    public async Task DiskteKiKayitlar_KuyrugaYuklenir()
    {
        await _fileStore.SaveAsync("1000691", [Telemetry("1000691", 1)], default);

        var queue = CreateQueue();
        var sut = CreateSut(queue);

        var count = await sut.RestoreAsync(default);

        Assert.Equal(1, count);
        Assert.Equal(1, queue.Count("1000691"));
    }

    [Fact]
    public async Task BirdenFazlaCihaz_AyriAyriYuklenir()
    {
        await _fileStore.SaveAsync("A", [Telemetry("A", 1), Telemetry("A", 2)], default);
        await _fileStore.SaveAsync("B", [Telemetry("B", 1)], default);

        var queue = CreateQueue();
        var sut = CreateSut(queue);

        var count = await sut.RestoreAsync(default);

        Assert.Equal(3, count);
        Assert.Equal(2, queue.Count("A"));
        Assert.Equal(1, queue.Count("B"));
    }

    [Fact]
    public async Task YuklenenKayitlar_TarihSirasindaOlur()
    {
        await _fileStore.SaveAsync(
            "A",
            [Telemetry("A", 3), Telemetry("A", 1), Telemetry("A", 2)],
            default);

        var queue = CreateQueue();
        await CreateSut(queue).RestoreAsync(default);

        var snapshot = queue.Snapshot("A");

        Assert.Equal(Base.AddSeconds(1), snapshot[0].DataDate);
        Assert.Equal(Base.AddSeconds(2), snapshot[1].DataDate);
        Assert.Equal(Base.AddSeconds(3), snapshot[2].DataDate);
    }

    [Fact]
    public async Task KapasiteAsilirsa_EnYeniKayitlarTutulur()
    {
        DeviceTelemetry[] items =
        [
            Telemetry("A", 1),
            Telemetry("A", 2),
            Telemetry("A", 3),
            Telemetry("A", 4)
        ];

        await _fileStore.SaveAsync("A", items, default);

        var queue = CreateQueue(capacity: 2);
        var count = await CreateSut(queue).RestoreAsync(default);

        Assert.Equal(2, count);

        var snapshot = queue.Snapshot("A");
        Assert.Equal(Base.AddSeconds(3), snapshot[0].DataDate);
        Assert.Equal(Base.AddSeconds(4), snapshot[1].DataDate);
    }

    [Fact]
    public async Task BozukDosya_DigerCihazlarinYuklenmesiniEngellemez()
    {
        await _fileStore.SaveAsync("bozuk", [Telemetry("bozuk", 1)], default);
        await _fileStore.SaveAsync("saglam", [Telemetry("saglam", 1)], default);

        await File.WriteAllTextAsync(
            Path.Combine(_directory, "bozuk.json"),
            "gecersiz icerik");

        var queue = CreateQueue();
        var count = await CreateSut(queue).RestoreAsync(default);

        Assert.Equal(1, count);
        Assert.Equal(1, queue.Count("saglam"));
        Assert.Equal(0, queue.Count("bozuk"));
    }

    [Fact]
    public async Task YazDurdurBaslatOku_VeriKaybiOlmaz()
    {
        var original = CreateQueue();
        original.Enqueue("A", Telemetry("A", 1));
        original.Enqueue("A", Telemetry("A", 2));

        await _fileStore.SaveAsync("A", original.Snapshot("A"), default);

        var restarted = CreateQueue();
        await CreateSut(restarted).RestoreAsync(default);

        Assert.Equal(original.Snapshot("A"), restarted.Snapshot("A"));
    }

    [Fact]
    public async Task VeritabaniKapaliyken_YazmaDiskeGider()
    {
        var store = CreateStore(databaseEnabled: false);

        await store.SaveAsync("A", [Telemetry("A", 1)], default);

        var loaded = await _fileStore.LoadAsync("A", default);
        Assert.Single(loaded);
    }
}