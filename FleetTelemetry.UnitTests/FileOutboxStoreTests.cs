using System.Text;
using FleetTelemetry.Application.Options;
using FleetTelemetry.Domain;
using FleetTelemetry.Infrastructure.Outbox;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.UnitTests;

public sealed class FileOutboxStoreTests : IDisposable
{
    private const string Device = "1000691";

    private static readonly DateTimeOffset Base =
        new(2026, 1, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly string _directory;
    private readonly FileOutboxStore _sut;

    public FileOutboxStoreTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "fleet-telemetry-tests",
            Guid.NewGuid().ToString("N"));

        var options = Options.Create(new PersistenceOptions
        {
            FileFallbackDirectory = _directory
        });

        _sut = new FileOutboxStore(options, NullLogger<FileOutboxStore>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static DeviceTelemetry Telemetry(int secondsOffset) => new()
    {
        DeviceCode = Device,
        DataDate = Base.AddSeconds(secondsOffset),
        GpsLat = 40.921852,
        GpsLon = 38.320351,
        Speed = 65.5,
        Angle = 133.0,
        Direction = TravelDirection.SouthEast,
        IsOnline = true
    };

    [Fact]
    public async Task YazilanKayitlar_AyniSekildeOkunur()
    {
        await _sut.SaveAsync(Device, [Telemetry(1), Telemetry(2)], default);

        var loaded = await _sut.LoadAsync(Device, default);

        Assert.Equal(2, loaded.Count);
        Assert.Equal(Base.AddSeconds(1), loaded[0].DataDate);
        Assert.Equal(Base.AddSeconds(2), loaded[1].DataDate);
    }

    [Fact]
    public async Task TumAlanlar_KorunarakGeriYuklenir()
    {
        var original = Telemetry(1);

        await _sut.SaveAsync(Device, [original], default);
        var loaded = await _sut.LoadAsync(Device, default);

        Assert.Equal(original, loaded[0]);
    }

    [Fact]
    public async Task OlmayanCihaz_BosListeDoner()
    {
        var loaded = await _sut.LoadAsync("bilinmeyen", default);

        Assert.Empty(loaded);
    }

    [Fact]
    public async Task IkinciYazma_OncekiniTamamenDegistirir()
    {
        await _sut.SaveAsync(Device, [Telemetry(1), Telemetry(2)], default);
        await _sut.SaveAsync(Device, [Telemetry(9)], default);

        var loaded = await _sut.LoadAsync(Device, default);

        Assert.Single(loaded);
        Assert.Equal(Base.AddSeconds(9), loaded[0].DataDate);
    }

    [Fact]
    public async Task BosListeYazilirsa_DosyaSilinir()
    {
        await _sut.SaveAsync(Device, [Telemetry(1)], default);
        await _sut.SaveAsync(Device, [], default);

        Assert.Empty(await _sut.ListDeviceCodesAsync(default));
        Assert.Empty(await _sut.LoadAsync(Device, default));
    }

    [Fact]
    public async Task GeciciDosya_YazimSonrasiKalmaz()
    {
        await _sut.SaveAsync(Device, [Telemetry(1)], default);

        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task CihazListesi_YazilanTumCihazlariDoner()
    {
        await _sut.SaveAsync("1000691", [Telemetry(1)], default);
        await _sut.SaveAsync("1001213", [Telemetry(1)], default);

        var codes = await _sut.ListDeviceCodesAsync(default);

        Assert.Equal(2, codes.Count);
        Assert.Contains("1000691", codes);
        Assert.Contains("1001213", codes);
    }

    [Fact]
    public async Task Temizleme_CihazDosyasiniSiler()
    {
        await _sut.SaveAsync(Device, [Telemetry(1)], default);
        await _sut.ClearAsync(Device, default);

        Assert.Empty(await _sut.LoadAsync(Device, default));
        Assert.Empty(await _sut.ListDeviceCodesAsync(default));
    }

    [Fact]
    public async Task BozukDosya_KarantinayaAlinirVeBosDoner()
    {
        await _sut.SaveAsync(Device, [Telemetry(1)], default);

        var path = Path.Combine(_directory, Device + ".json");
        await File.WriteAllTextAsync(path, "{bu gecerli json degil", Encoding.UTF8);

        var loaded = await _sut.LoadAsync(Device, default);

        Assert.Empty(loaded);
        Assert.True(File.Exists(path + ".corrupt"));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task BozukDosya_DigerCihazlariEtkilemez()
    {
        await _sut.SaveAsync("bozuk", [Telemetry(1)], default);
        await _sut.SaveAsync("saglam", [Telemetry(5)], default);

        await File.WriteAllTextAsync(
            Path.Combine(_directory, "bozuk.json"),
            "cop veri",
            Encoding.UTF8);

        Assert.Empty(await _sut.LoadAsync("bozuk", default));
        Assert.Single(await _sut.LoadAsync("saglam", default));
    }

    [Fact]
    public async Task AyniCihazaEsZamanliYazma_DosyayiBozmaz()
    {
        var tasks = Enumerable
            .Range(1, 30)
            .Select(i => _sut.SaveAsync(Device, [Telemetry(i)], default))
            .ToArray();

        await Task.WhenAll(tasks);

        var loaded = await _sut.LoadAsync(Device, default);

        Assert.Single(loaded);
    }

    [Fact]
    public async Task FarkliCihazlaraEsZamanliYazma_HepsiKorunur()
    {
        var tasks = Enumerable
            .Range(0, 20)
            .Select(i => _sut.SaveAsync($"cihaz{i}", [Telemetry(i)], default))
            .ToArray();

        await Task.WhenAll(tasks);

        var codes = await _sut.ListDeviceCodesAsync(default);

        Assert.Equal(20, codes.Count);
    }

    [Fact]
    public async Task GecersizKarakterliCihazKodu_DosyaAdinaDonusturulur()
    {
        var code = "cihaz/kod:1";

        await _sut.SaveAsync(code, [Telemetry(1)], default);
        var loaded = await _sut.LoadAsync(code, default);

        Assert.Single(loaded);
    }
}