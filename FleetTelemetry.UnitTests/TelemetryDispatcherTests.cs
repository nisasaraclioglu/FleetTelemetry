using FleetTelemetry.Application.Abstractions;
using FleetTelemetry.Application.Dispatching;
using FleetTelemetry.Application.Options;
using FleetTelemetry.Application.Policies;
using FleetTelemetry.Domain;
using FleetTelemetry.Infrastructure.Outbox;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace FleetTelemetry.UnitTests;

public sealed class TelemetryDispatcherTests
{
    private static readonly DateTimeOffset Base =
        new(2026, 1, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new();
    private readonly FakeRegistry _registry = new();
    private readonly BoundedOutboxQueue _queue;

    public TelemetryDispatcherTests()
    {
        _clock.SetUtcNow(Base);
        _queue = new BoundedOutboxQueue(
            Options.Create(new OutboxOptions { MaxItemsPerDevice = 100 }));
    }

    private TelemetryDispatcher CreateSut(
        int parallelism = 4,
        int maxPerDevice = 10)
    {
        var policy = new ContactBasedSendPolicy(
            Options.Create(new SendPolicyOptions()),
            _clock);

        var options = Options.Create(new PollingOptions
        {
            MaxDegreeOfParallelism = parallelism,
            MaxMessagesPerDevicePerTick = maxPerDevice,
            DelayBetweenMessages = TimeSpan.Zero
        });

        return new TelemetryDispatcher(
            policy,
            _queue,
            _registry,
            options,
            _clock,
            NullLogger<TelemetryDispatcher>.Instance);
    }

    private static DeviceTelemetry Telemetry(
        string deviceCode,
        bool isOnline = true,
        int secondsOffset = 0) => new()
        {
            DeviceCode = deviceCode,
            DataDate = Base.AddSeconds(secondsOffset),
            GpsLat = 40.9,
            GpsLon = 38.3,
            Speed = 60,
            Angle = 90,
            IsOnline = isOnline
        };

    private static DeviceTelemetry[] Devices(int count) =>
        Enumerable.Range(1, count)
            .Select(i => Telemetry($"cihaz{i}"))
            .ToArray();

    [Fact]
    public async Task IlkTurda_TumCihazlarGonderilir()
    {
        var sut = CreateSut();

        var summary = await sut.DispatchAsync(Devices(5), default);

        Assert.Equal(5, summary.Total);
        Assert.Equal(5, summary.Sent);
        Assert.Equal(0, summary.Failed);
    }

    [Fact]
    public async Task EszamanliCalismaSayisi_SiniriAsmaz()
    {
        _registry.TrackConcurrency = true;
        var sut = CreateSut(parallelism: 4);

        await sut.DispatchAsync(Devices(40), default);

        Assert.True(
            _registry.MaxObservedConcurrency <= 4,
            $"Eşzamanlılık {_registry.MaxObservedConcurrency} oldu, beklenen en fazla 4");
    }

    [Fact]
    public async Task ParalellikBir_TekTekIsler()
    {
        _registry.TrackConcurrency = true;
        var sut = CreateSut(parallelism: 1);

        await sut.DispatchAsync(Devices(10), default);

        Assert.Equal(1, _registry.MaxObservedConcurrency);
    }

    [Fact]
    public async Task TekCihazinHatasi_DigerleriniEtkilemez()
    {
        _registry.FailForDevice = "cihaz3";
        var sut = CreateSut();

        var summary = await sut.DispatchAsync(Devices(5), default);

        Assert.Equal(4, summary.Sent);
        Assert.Equal(1, summary.Failed);
    }

    [Fact]
    public async Task TekCihazinIstisnasi_TuruDusurmez()
    {
        _registry.ThrowForDevice = "cihaz2";
        var sut = CreateSut();

        var summary = await sut.DispatchAsync(Devices(4), default);

        Assert.Equal(3, summary.Sent);
        Assert.Equal(1, summary.Failed);
    }

    [Fact]
    public async Task BasarisizGonderim_VeriyiKuyruktaBirakir()
    {
        _registry.FailForDevice = "cihaz1";
        var sut = CreateSut();

        await sut.DispatchAsync([Telemetry("cihaz1")], default);

        Assert.Equal(1, _queue.Count("cihaz1"));
    }

    [Fact]
    public async Task BasariliGonderim_KuyrugaBirakmaz()
    {
        var sut = CreateSut();

        await sut.DispatchAsync([Telemetry("cihaz1")], default);

        Assert.Equal(0, _queue.Count("cihaz1"));
    }

    [Fact]
    public async Task TurBasinaGonderimSiniri_Uygulanir()
    {
        for (var i = 0; i < 20; i++)
        {
            _queue.Enqueue("cihaz1", Telemetry("cihaz1", secondsOffset: i));
        }

        var sut = CreateSut(maxPerDevice: 5);

        await sut.DispatchAsync([Telemetry("cihaz1", secondsOffset: 100)], default);

        Assert.Equal(5, _registry.SentCountFor("cihaz1"));
        Assert.Equal(16, _queue.Count("cihaz1"));
    }

    [Fact]
    public async Task SureDolmadan_PolitikaGonderimiEngeller()
    {
        var sut = CreateSut();
        var telemetry = Telemetry("cihaz1");

        await sut.DispatchAsync([telemetry], default);

        _clock.Advance(TimeSpan.FromSeconds(3));
        var summary = await sut.DispatchAsync([telemetry], default);

        Assert.Equal(1, summary.SkippedByPolicy);
        Assert.Equal(0, summary.Sent);
    }

    [Fact]
    public async Task SureDolunca_PolitikaGonderimeIzinVerir()
    {
        var sut = CreateSut();

        await sut.DispatchAsync([Telemetry("cihaz1")], default);

        _clock.Advance(TimeSpan.FromSeconds(11));
        var summary = await sut.DispatchAsync([Telemetry("cihaz1")], default);

        Assert.Equal(1, summary.Sent);
    }

    [Fact]
    public async Task KontakDegisimi_SureBeklemedenGonderir()
    {
        var sut = CreateSut();

        await sut.DispatchAsync([Telemetry("cihaz1", isOnline: true)], default);

        _clock.Advance(TimeSpan.FromSeconds(2));
        var summary = await sut.DispatchAsync(
            [Telemetry("cihaz1", isOnline: false)],
            default);

        Assert.Equal(1, summary.Sent);
    }

    [Fact]
    public async Task BosListe_HatasizTamamlanir()
    {
        var sut = CreateSut();

        var summary = await sut.DispatchAsync([], default);

        Assert.Equal(0, summary.Total);
        Assert.Equal(0, summary.Sent);
    }

    private sealed class FakeRegistry : IDeviceConnectionRegistry
    {
        private readonly Dictionary<string, FakeConnection> _connections = new();
        private readonly Lock _gate = new();

        private int _currentConcurrency;

        public bool TrackConcurrency { get; set; }
        public int MaxObservedConcurrency { get; private set; }
        public string? FailForDevice { get; set; }
        public string? ThrowForDevice { get; set; }

        public int Count => _connections.Count;

        public int SentCountFor(string deviceCode) =>
            _connections.TryGetValue(deviceCode, out var c) ? c.SentCount : 0;

        public IDeviceConnection GetOrCreate(string deviceCode)
        {
            lock (_gate)
            {
                if (!_connections.TryGetValue(deviceCode, out var connection))
                {
                    connection = new FakeConnection(deviceCode, this);
                    _connections[deviceCode] = connection;
                }

                return connection;
            }
        }

        public bool TryGet(string deviceCode, out IDeviceConnection connection)
        {
            lock (_gate)
            {
                if (_connections.TryGetValue(deviceCode, out var found))
                {
                    connection = found;
                    return true;
                }

                connection = default!;
                return false;
            }
        }

        public Task<bool> RemoveAsync(string deviceCode)
        {
            lock (_gate)
            {
                return Task.FromResult(_connections.Remove(deviceCode));
            }
        }

        public IReadOnlyCollection<IDeviceConnection> Snapshot()
        {
            lock (_gate)
            {
                return _connections.Values.ToArray<IDeviceConnection>();
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        internal void EnterSend()
        {
            if (!TrackConcurrency)
            {
                return;
            }

            lock (_gate)
            {
                _currentConcurrency++;

                if (_currentConcurrency > MaxObservedConcurrency)
                {
                    MaxObservedConcurrency = _currentConcurrency;
                }
            }
        }

        internal void ExitSend()
        {
            if (!TrackConcurrency)
            {
                return;
            }

            lock (_gate)
            {
                _currentConcurrency--;
            }
        }
    }

    private sealed class FakeConnection : IDeviceConnection
    {
        private readonly FakeRegistry _owner;

        public FakeConnection(string deviceCode, FakeRegistry owner)
        {
            DeviceCode = deviceCode;
            _owner = owner;
        }

        public string DeviceCode { get; }

        public ConnectionState State => ConnectionState.Connected;

        public DateTimeOffset LastActivityAt => Base;

        public int SentCount { get; private set; }

        public async Task<SendOutcome> SendAsync(
            DeviceTelemetry telemetry,
            CancellationToken ct)
        {
            _owner.EnterSend();

            try
            {
                await Task.Delay(5, ct);

                if (_owner.ThrowForDevice == DeviceCode)
                {
                    throw new InvalidOperationException("beklenmeyen hata");
                }

                if (_owner.FailForDevice == DeviceCode)
                {
                    return SendOutcome.SendFailed;
                }

                SentCount++;
                return SendOutcome.Sent;
            }
            finally
            {
                _owner.ExitSend();
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}