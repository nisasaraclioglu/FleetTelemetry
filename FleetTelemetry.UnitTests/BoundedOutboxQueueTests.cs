using FleetTelemetry.Application.Options;
using FleetTelemetry.Domain;
using FleetTelemetry.Infrastructure.Outbox;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.UnitTests;

public sealed class BoundedOutboxQueueTests
{
    private const string Device = "1000691";

    private static readonly DateTimeOffset Base =
        new(2026, 1, 16, 12, 0, 0, TimeSpan.Zero);

    private static BoundedOutboxQueue CreateSut(int capacity = 3) =>
        new(Options.Create(new OutboxOptions { MaxItemsPerDevice = capacity }));

    private static DeviceTelemetry Telemetry(int secondsOffset) => new()
    {
        DeviceCode = Device,
        DataDate = Base.AddSeconds(secondsOffset),
        GpsLat = 40.9,
        GpsLon = 38.3,
        Speed = 65.5,
        Angle = 133.0,
        IsOnline = true
    };

    [Fact]
    public void EklenenKayit_SirayiKorur()
    {
        var sut = CreateSut();

        sut.Enqueue(Device, Telemetry(1));
        sut.Enqueue(Device, Telemetry(2));

        Assert.True(sut.TryDequeue(Device, out var first));
        Assert.True(sut.TryDequeue(Device, out var second));

        Assert.Equal(Base.AddSeconds(1), first.DataDate);
        Assert.Equal(Base.AddSeconds(2), second.DataDate);
    }

    [Fact]
    public void BosKuyruktan_CikarmaBasarisiz()
    {
        var sut = CreateSut();

        Assert.False(sut.TryDequeue(Device, out _));
    }

    [Fact]
    public void TanimsizCihaz_SifirDoner()
    {
        var sut = CreateSut();

        Assert.Equal(0, sut.Count("bilinmeyen"));
        Assert.Empty(sut.Snapshot("bilinmeyen"));
    }

    [Fact]
    public void KapasiteDolunca_EnEskiSilinir()
    {
        var sut = CreateSut(capacity: 3);

        sut.Enqueue(Device, Telemetry(1));
        sut.Enqueue(Device, Telemetry(2));
        sut.Enqueue(Device, Telemetry(3));
        var result = sut.Enqueue(Device, Telemetry(4));

        Assert.Equal(OutboxEnqueueResult.AddedWithEviction, result);
        Assert.Equal(3, sut.Count(Device));

        var snapshot = sut.Snapshot(Device);
        Assert.Equal(Base.AddSeconds(2), snapshot[0].DataDate);
        Assert.Equal(Base.AddSeconds(4), snapshot[2].DataDate);
    }

    [Fact]
    public void KapasiteDolmadan_AtmaOlmaz()
    {
        var sut = CreateSut(capacity: 3);

        var result = sut.Enqueue(Device, Telemetry(1));

        Assert.Equal(OutboxEnqueueResult.Added, result);
        Assert.Equal(0, sut.TotalEvicted);
    }

    [Fact]
    public void AtilanKayitlar_Sayilir()
    {
        var sut = CreateSut(capacity: 2);

        sut.Enqueue(Device, Telemetry(1));
        sut.Enqueue(Device, Telemetry(2));
        sut.Enqueue(Device, Telemetry(3));
        sut.Enqueue(Device, Telemetry(4));

        Assert.Equal(2, sut.TotalEvicted);
    }

    [Fact]
    public void GeriKonanKayit_SiradanAlinir()
    {
        var sut = CreateSut();

        sut.Enqueue(Device, Telemetry(1));
        sut.Enqueue(Device, Telemetry(2));
        sut.TryDequeue(Device, out var taken);

        sut.Requeue(Device, taken);

        Assert.True(sut.TryDequeue(Device, out var next));
        Assert.Equal(Base.AddSeconds(1), next.DataDate);
    }

    [Fact]
    public void KuyrukDoluykenGeriKoyma_YoksayilirVeYeniKayitKorunur()
    {
        var sut = CreateSut(capacity: 2);

        sut.Enqueue(Device, Telemetry(1));
        sut.Enqueue(Device, Telemetry(2));

        sut.Requeue(Device, Telemetry(0));

        Assert.Equal(2, sut.Count(Device));
        Assert.Equal(Base.AddSeconds(1), sut.Snapshot(Device)[0].DataDate);
    }

    [Fact]
    public void Yukleme_TarihSirasinaGoreDuzenler()
    {
        var sut = CreateSut(capacity: 5);

        sut.Load(Device, [Telemetry(3), Telemetry(1), Telemetry(2)]);

        var snapshot = sut.Snapshot(Device);

        Assert.Equal(Base.AddSeconds(1), snapshot[0].DataDate);
        Assert.Equal(Base.AddSeconds(2), snapshot[1].DataDate);
        Assert.Equal(Base.AddSeconds(3), snapshot[2].DataDate);
    }

    [Fact]
    public void Yukleme_KapasiteAsiliyorsaEnYeniKayitlariTutar()
    {
        var sut = CreateSut(capacity: 2);

        sut.Load(Device, [Telemetry(1), Telemetry(2), Telemetry(3)]);

        var snapshot = sut.Snapshot(Device);

        Assert.Equal(2, snapshot.Count);
        Assert.Equal(Base.AddSeconds(2), snapshot[0].DataDate);
        Assert.Equal(Base.AddSeconds(3), snapshot[1].DataDate);
    }

    [Fact]
    public void Yukleme_MevcutKuyrugunUzerineYazar()
    {
        var sut = CreateSut();

        sut.Enqueue(Device, Telemetry(9));
        sut.Load(Device, [Telemetry(1)]);

        Assert.Equal(1, sut.Count(Device));
        Assert.Equal(Base.AddSeconds(1), sut.Snapshot(Device)[0].DataDate);
    }

    [Fact]
    public void CihazlarBirbirindenBagimsizdir()
    {
        var sut = CreateSut(capacity: 1);

        sut.Enqueue("A", Telemetry(1));
        sut.Enqueue("B", Telemetry(2));
        sut.Enqueue("A", Telemetry(3));

        Assert.Equal(1, sut.Count("A"));
        Assert.Equal(1, sut.Count("B"));
        Assert.Equal(Base.AddSeconds(2), sut.Snapshot("B")[0].DataDate);
        Assert.Contains("A", sut.DeviceCodes);
        Assert.Contains("B", sut.DeviceCodes);
    }

    [Fact]
    public void SnapshotKopyaDoner_SonrakiDegisiklikEtkilemez()
    {
        var sut = CreateSut();

        sut.Enqueue(Device, Telemetry(1));
        var snapshot = sut.Snapshot(Device);

        sut.Enqueue(Device, Telemetry(2));

        Assert.Single(snapshot);
    }
}