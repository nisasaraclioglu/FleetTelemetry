using FleetTelemetry.Application.Abstractions;
using FleetTelemetry.Application.Options;
using FleetTelemetry.Domain;
using FleetTelemetry.Infrastructure.Resilience;
using FleetTelemetry.Infrastructure.Tcp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace FleetTelemetry.UnitTests;

public sealed class DeviceConnectionRegistryTests
{
    private readonly FakeTimeProvider _clock = new();
    private readonly CountingConnectionFactory _factory = new();

    private DeviceConnectionRegistry CreateSut()
    {
        _clock.SetUtcNow(new DateTimeOffset(2026, 1, 16, 12, 0, 0, TimeSpan.Zero));

        return new DeviceConnectionRegistry(
            _factory,
            new StubEncoder(),
            new ReconnectPolicy(Options.Create(new ReconnectOptions())),
            Options.Create(new TcpOptions()),
            _clock,
            NullLoggerFactory.Instance);
    }

    [Fact]
    public void AyniCihazIcin_TekNesneDoner()
    {
        var sut = CreateSut();

        var first = sut.GetOrCreate("1000691");
        var second = sut.GetOrCreate("1000691");

        Assert.Same(first, second);
        Assert.Equal(1, sut.Count);
    }

    [Fact]
    public void FarkliCihazlar_AyriNesneAlir()
    {
        var sut = CreateSut();

        var first = sut.GetOrCreate("1000691");
        var second = sut.GetOrCreate("1001213");

        Assert.NotSame(first, second);
        Assert.Equal(2, sut.Count);
    }

    [Fact]
    public void NesneOlusturma_SoketAcmaz()
    {
        var sut = CreateSut();

        sut.GetOrCreate("1000691");

        Assert.Equal(0, _factory.CreatedCount);
    }

    [Fact]
    public void TanimsizCihaz_TryGetBasarisizDoner()
    {
        var sut = CreateSut();

        Assert.False(sut.TryGet("yok", out _));
    }

    [Fact]
    public void OlusturulanCihaz_TryGetIleBulunur()
    {
        var sut = CreateSut();
        var created = sut.GetOrCreate("1000691");

        Assert.True(sut.TryGet("1000691", out var found));
        Assert.Same(created, found);
    }

    [Fact]
    public async Task Silme_BaglantiyiKapatirVeSayiyiAzaltir()
    {
        var sut = CreateSut();
        var connection = sut.GetOrCreate("1000691");

        var removed = await sut.RemoveAsync("1000691");

        Assert.True(removed);
        Assert.Equal(0, sut.Count);
        Assert.Equal(ConnectionState.Disposed, connection.State);
    }

    [Fact]
    public async Task OlmayanCihazinSilinmesi_FalseDoner()
    {
        var sut = CreateSut();

        Assert.False(await sut.RemoveAsync("yok"));
    }

    [Fact]
    public void Snapshot_TumBaglantilariDoner()
    {
        var sut = CreateSut();
        sut.GetOrCreate("A");
        sut.GetOrCreate("B");

        var snapshot = sut.Snapshot();

        Assert.Equal(2, snapshot.Count);
    }

    [Fact]
    public async Task Kapatma_TumBaglantilariKapatir()
    {
        var sut = CreateSut();
        var first = sut.GetOrCreate("A");
        var second = sut.GetOrCreate("B");

        await sut.DisposeAsync();

        Assert.Equal(ConnectionState.Disposed, first.State);
        Assert.Equal(ConnectionState.Disposed, second.State);
        Assert.Equal(0, sut.Count);
    }

    [Fact]
    public async Task KapatildiktanSonra_YeniBaglantiOlusturulamaz()
    {
        var sut = CreateSut();
        await sut.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => sut.GetOrCreate("A"));
    }

    [Fact]
    public async Task CiftKapatma_HataVermez()
    {
        var sut = CreateSut();
        sut.GetOrCreate("A");

        await sut.DisposeAsync();
        await sut.DisposeAsync();

        Assert.Equal(0, sut.Count);
    }

    private sealed class StubEncoder : ITelemetryEncoder
    {
        public ReadOnlyMemory<byte> Encode(DeviceTelemetry telemetry) => new byte[] { 1 };
    }

    private sealed class CountingConnectionFactory : ITcpConnectionFactory
    {
        public int CreatedCount { get; private set; }

        public ITcpConnection Create()
        {
            CreatedCount++;
            return new StubConnection();
        }
    }

    private sealed class StubConnection : ITcpConnection
    {
        public Task ConnectAsync(string host, int port, CancellationToken ct) =>
            Task.CompletedTask;

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}