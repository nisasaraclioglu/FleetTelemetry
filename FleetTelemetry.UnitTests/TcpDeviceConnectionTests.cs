using FleetTelemetry.Application.Abstractions;
using FleetTelemetry.Application.Options;
using FleetTelemetry.Domain;
using FleetTelemetry.Infrastructure.Resilience;
using FleetTelemetry.Infrastructure.Tcp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace FleetTelemetry.UnitTests;

public sealed class TcpDeviceConnectionTests
{
    private const string Device = "1000691";

    private static readonly DateTimeOffset Base =
        new(2026, 1, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new();
    private readonly FakeConnectionFactory _factory = new();

    private TcpDeviceConnection CreateSut()
    {
        _clock.SetUtcNow(Base);

        var reconnectOptions = Options.Create(new ReconnectOptions
        {
            InitialDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(30),
            BackoffMultiplier = 2.0,
            JitterRatio = 0.0
        });

        return new TcpDeviceConnection(
            Device,
            _factory,
            new FakeEncoder(),
            new ReconnectPolicy(reconnectOptions),
            new TcpOptions(),
            _clock,
            NullLogger.Instance);
    }

    private static DeviceTelemetry Telemetry() =>  new()
    {
        DeviceCode = Device,
        DataDate = Base,
        GpsLat = 40.9,
        GpsLon = 38.3,
        Speed = 60,
        Angle = 90,
        IsOnline = true
    };

    [Fact]
    public async Task BaslangictaBaglantiKurulmaz()
    {
        var sut = CreateSut();

        Assert.Equal(ConnectionState.Disconnected, sut.State);
        Assert.Equal(0, _factory.CreatedCount);
    }

    [Fact]
    public async Task IlkGonderimde_BaglantiKurulur()
    {
        var sut = CreateSut();

        var result = await sut.SendAsync(Telemetry(), default);

        Assert.Equal(SendOutcome.Sent, result);
        Assert.Equal(ConnectionState.Connected, sut.State);
        Assert.Equal(1, _factory.CreatedCount);
    }

    [Fact]
    public async Task IkinciGonderimde_AyniBaglantiKullanilir()
    {
        var sut = CreateSut();

        await sut.SendAsync(Telemetry(), default);
        await sut.SendAsync(Telemetry(), default);

        Assert.Equal(1, _factory.CreatedCount);
        Assert.Equal(2, _factory.Last!.SentCount);
    }

    [Fact]
    public async Task BaglanmaHatasi_FaultedDurumunaGecirir()
    {
        _factory.FailConnect = true;
        var sut = CreateSut();

        var result = await sut.SendAsync(Telemetry(), default);

        Assert.Equal(SendOutcome.ConnectFailed, result);
        Assert.Equal(ConnectionState.Faulted, sut.State);
    }

    [Fact]
    public async Task BaglanmaHatasi_SoketiSerbestBirakir()
    {
        _factory.FailConnect = true;
        var sut = CreateSut();

        await sut.SendAsync(Telemetry(), default);

        Assert.True(_factory.Last!.Disposed);
    }

    [Fact]
    public async Task GonderimHatasi_SoketiKapatirVeFaultedYapar()
    {
        _factory.FailSend = true;
        var sut = CreateSut();

        var result = await sut.SendAsync(Telemetry(), default);

        Assert.Equal(SendOutcome.SendFailed, result);
        Assert.Equal(ConnectionState.Faulted, sut.State);
        Assert.True(_factory.Last!.Disposed);
    }

    [Fact]
    public async Task BackoffSuresiDolmadan_YenidenDenenmez()
    {
        _factory.FailConnect = true;
        var sut = CreateSut();

        await sut.SendAsync(Telemetry(), default);
        var createdAfterFirst = _factory.CreatedCount;

        var result = await sut.SendAsync(Telemetry(), default);

        Assert.Equal(SendOutcome.SkippedBackoff, result);
        Assert.Equal(createdAfterFirst, _factory.CreatedCount);
    }

    [Fact]
    public async Task BackoffSuresiDolunca_YenidenDenenir()
    {
        _factory.FailConnect = true;
        var sut = CreateSut();

        await sut.SendAsync(Telemetry(), default);

        _clock.Advance(TimeSpan.FromSeconds(2));
        _factory.FailConnect = false;

        var result = await sut.SendAsync(Telemetry(), default);

        Assert.Equal(SendOutcome.Sent, result);
        Assert.Equal(ConnectionState.Connected, sut.State);
    }

    [Fact]
    public async Task BackoffSuresi_HerHatadaArtar()
    {
        _factory.FailConnect = true;
        var sut = CreateSut();

        await sut.SendAsync(Telemetry(), default);

        _clock.Advance(TimeSpan.FromSeconds(1));
        await sut.SendAsync(Telemetry(), default);

        _clock.Advance(TimeSpan.FromSeconds(1));
        var result = await sut.SendAsync(Telemetry(), default);

        Assert.Equal(SendOutcome.SkippedBackoff, result);
    }

    [Fact]
    public async Task BasariliGonderim_BackoffSayaciniSifirlar()
    {
        _factory.FailConnect = true;
        var sut = CreateSut();

        await sut.SendAsync(Telemetry(), default);

        _clock.Advance(TimeSpan.FromSeconds(2));
        _factory.FailConnect = false;
        await sut.SendAsync(Telemetry(), default);

        _factory.FailSend = true;
        await sut.SendAsync(Telemetry(), default);

        _clock.Advance(TimeSpan.FromSeconds(1));
        _factory.FailSend = false;

        var result = await sut.SendAsync(Telemetry(), default);

        Assert.Equal(SendOutcome.Sent, result);
    }

    [Fact]
    public async Task KapatildiktanSonra_GonderimYapilmaz()
    {
        var sut = CreateSut();
        await sut.SendAsync(Telemetry(), default);

        await sut.DisposeAsync();

        var result = await sut.SendAsync(Telemetry(), default);

        Assert.Equal(SendOutcome.Disposed, result);
        Assert.Equal(ConnectionState.Disposed, sut.State);
    }

    [Fact]
    public async Task Kapatma_SoketiSerbestBirakir()
    {
        var sut = CreateSut();
        await sut.SendAsync(Telemetry(), default);

        await sut.DisposeAsync();

        Assert.True(_factory.Last!.Disposed);
    }

    [Fact]
    public async Task CiftKapatma_HataVermez()
    {
        var sut = CreateSut();
        await sut.SendAsync(Telemetry(), default);

        await sut.DisposeAsync();
        await sut.DisposeAsync();

        Assert.Equal(ConnectionState.Disposed, sut.State);
    }

    [Fact]
    public async Task BasariliGonderim_SonAktifligiGunceller()
    {
        var sut = CreateSut();

        _clock.Advance(TimeSpan.FromMinutes(5));
        await sut.SendAsync(Telemetry(), default);

        Assert.Equal(Base.AddMinutes(5), sut.LastActivityAt);
    }

    [Fact]
    public async Task BasarisizGonderim_SonAktifligiGuncellemez()
    {
        _factory.FailConnect = true;
        var sut = CreateSut();

        _clock.Advance(TimeSpan.FromMinutes(5));
        await sut.SendAsync(Telemetry(), default);

        Assert.Equal(Base, sut.LastActivityAt);
    }

    [Fact]
    public async Task KopanBaglanti_YeniSoketleKurulur()
    {
        var sut = CreateSut();
        await sut.SendAsync(Telemetry(), default);

        _factory.FailSend = true;
        await sut.SendAsync(Telemetry(), default);

        _clock.Advance(TimeSpan.FromSeconds(2));
        _factory.FailSend = false;
        await sut.SendAsync(Telemetry(), default);

        Assert.Equal(2, _factory.CreatedCount);
    }

    private sealed class FakeEncoder : ITelemetryEncoder
    {
        public ReadOnlyMemory<byte> Encode(DeviceTelemetry telemetry) =>
            new byte[] { 1, 2, 3 };
    }

    private sealed class FakeConnectionFactory : ITcpConnectionFactory
    {
        public bool FailConnect { get; set; }

        public bool FailSend { get; set; }

        public int CreatedCount { get; private set; }

        public FakeConnection? Last { get; private set; }

        public ITcpConnection Create()
        {
            CreatedCount++;
            Last = new FakeConnection(this);
            return Last;
        }
    }

    private sealed class FakeConnection : ITcpConnection
    {
        private readonly FakeConnectionFactory _owner;

        public FakeConnection(FakeConnectionFactory owner) => _owner = owner;

        public int SentCount { get; private set; }

        public bool Disposed { get; private set; }

        public Task ConnectAsync(string host, int port, CancellationToken ct) =>
            _owner.FailConnect
                ? Task.FromException(new IOException("bağlanma başarısız"))
                : Task.CompletedTask;

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
        {
            if (_owner.FailSend)
            {
                return Task.FromException(new IOException("gönderim başarısız"));
            }

            SentCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}