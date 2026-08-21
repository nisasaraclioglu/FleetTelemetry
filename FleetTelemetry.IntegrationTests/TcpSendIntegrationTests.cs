using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using FleetTelemetry.Application.Options;
using FleetTelemetry.Domain;
using FleetTelemetry.Infrastructure.Resilience;
using FleetTelemetry.Infrastructure.Tcp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.IntegrationTests;

public sealed class TcpSendIntegrationTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Base =
        new(2026, 1, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly TestServer _server = new();

    public async ValueTask DisposeAsync() => await _server.DisposeAsync();

    private TcpDeviceConnection CreateSut(int port)
    {
        var tcpOptions = new TcpOptions
        {
            Host = "127.0.0.1",
            Port = port,
            ConnectTimeout = TimeSpan.FromSeconds(3),
            SendTimeout = TimeSpan.FromSeconds(3)
        };

        var reconnectOptions = Options.Create(new ReconnectOptions
        {
            InitialDelay = TimeSpan.FromMilliseconds(50),
            MaxDelay = TimeSpan.FromMilliseconds(200),
            BackoffMultiplier = 2.0,
            JitterRatio = 0.0
        });

        return new TcpDeviceConnection(
            "1000691",
            new SocketTcpConnectionFactory(Options.Create(tcpOptions)),
            new PlaceholderJsonEncoder(Options.Create(new TimeZoneOptions())),
            new ReconnectPolicy(reconnectOptions),
            tcpOptions,
            TimeProvider.System,
            NullLogger.Instance);
    }

    private static DeviceTelemetry Telemetry(int secondsOffset = 0) => new()
    {
        DeviceCode = "1000691",
        DeviceName = "34NKZ689",
        DataDate = Base.AddSeconds(secondsOffset),
        GpsLat = 40.921852,
        GpsLon = 38.320351,
        Speed = 65.5,
        Angle = 133.0,
        Direction = TravelDirection.SouthEast,
        IsOnline = true
    };

    [Fact]
    public async Task GercekSokete_VeriGonderilir()
    {
        _server.Start();
        await using var sut = CreateSut(_server.Port);

        var result = await sut.SendAsync(Telemetry(), default);

        Assert.Equal(SendOutcome.Sent, result);

        var message = await _server.WaitForMessageAsync();
        Assert.Contains("1000691", message);
        Assert.Contains("34NKZ689", message);
    }

    [Fact]
    public async Task GonderilenMesaj_UzunlukOnekiIcerir()
    {
        _server.Start();
        await using var sut = CreateSut(_server.Port);

        await sut.SendAsync(Telemetry(), default);

        var frame = await _server.WaitForFrameAsync();
        var declaredLength = BinaryPrimitives.ReadInt32BigEndian(frame);

        Assert.Equal(frame.Length - 4, declaredLength);
    }

    [Fact]
    public async Task GonderilenMesaj_UtcUcOfsetiTasir()
    {
        _server.Start();
        await using var sut = CreateSut(_server.Port);

        await sut.SendAsync(Telemetry(), default);

        var message = await _server.WaitForMessageAsync();

        Assert.Contains("2026-01-16 15:00:00", message);
    }

    [Fact]
    public async Task SunucuYokken_BaglantiKurulamaz()
    {
        var freePort = TestServer.GetFreePort();
        await using var sut = CreateSut(freePort);

        var result = await sut.SendAsync(Telemetry(), default);

        Assert.Equal(SendOutcome.ConnectFailed, result);
        Assert.Equal(ConnectionState.Faulted, sut.State);
    }

    [Fact]
    public async Task SunucuKapanipAcilinca_KendiliğindenGeriDoner()
    {
        _server.Start();
        await using var sut = CreateSut(_server.Port);

        Assert.Equal(SendOutcome.Sent, await sut.SendAsync(Telemetry(1), default));

        _server.Stop();
        await Task.Delay(200);

        // Kopmuş bağlantıda ilk yazma işletim sistemi tamponuna gider ve
        // başarılı görünebilir; kopuş genellikle sonraki yazmada anlaşılır.
        await sut.SendAsync(Telemetry(2), default);
        await sut.SendAsync(Telemetry(3), default);

        Assert.Equal(ConnectionState.Faulted, sut.State);

        _server.Start();
        await Task.Delay(500);

        var recovered = await sut.SendAsync(Telemetry(4), default);
        Assert.Equal(SendOutcome.Sent, recovered);
        Assert.Equal(ConnectionState.Connected, sut.State);
    }

    [Fact]
    public async Task ArdisikGonderimler_AyniBaglantiyiKullanir()
    {
        _server.Start();
        await using var sut = CreateSut(_server.Port);

        await sut.SendAsync(Telemetry(1), default);
        await sut.SendAsync(Telemetry(2), default);
        await sut.SendAsync(Telemetry(3), default);

        await _server.WaitForMessageCountAsync(3);

        Assert.Equal(1, _server.AcceptedConnectionCount);
    }

    private sealed class TestServer : IAsyncDisposable
    {
        private readonly List<byte[]> _frames = [];
        private readonly Lock _gate = new();

        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private int _acceptedConnections;

        public int Port { get; private set; }

        public int AcceptedConnectionCount => _acceptedConnections;

        public static int GetFreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        public void Start()
        {
            if (Port == 0)
            {
                Port = GetFreePort();
            }

            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Loopback, Port);
            _listener.Start();

            _ = AcceptLoopAsync(_listener, _cts.Token);
        }

        public void Stop()
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listener = null;
        }

        private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(ct);
                    Interlocked.Increment(ref _acceptedConnections);
                    _ = ReadLoopAsync(client, ct);
                }
            }
            catch
            {
                // dinleyici durduruldu
            }
        }

        private async Task ReadLoopAsync(TcpClient client, CancellationToken ct)
        {
            try
            {
                await using var stream = client.GetStream();
                var header = new byte[4];

                while (!ct.IsCancellationRequested)
                {
                    await stream.ReadExactlyAsync(header, ct);
                    var length = BinaryPrimitives.ReadInt32BigEndian(header);

                    var body = new byte[length];
                    await stream.ReadExactlyAsync(body, ct);

                    var frame = new byte[4 + length];
                    header.CopyTo(frame, 0);
                    body.CopyTo(frame, 4);

                    lock (_gate)
                    {
                        _frames.Add(frame);
                    }
                }
            }
            catch
            {
                // bağlantı kapandı
            }
            finally
            {
                client.Dispose();
            }
        }

        public async Task<byte[]> WaitForFrameAsync()
        {
            await WaitForMessageCountAsync(1);

            lock (_gate)
            {
                return _frames[0];
            }
        }

        public async Task<string> WaitForMessageAsync()
        {
            var frame = await WaitForFrameAsync();
            return Encoding.UTF8.GetString(frame, 4, frame.Length - 4);
        }

        public async Task WaitForMessageCountAsync(int count)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);

            while (DateTime.UtcNow < deadline)
            {
                lock (_gate)
                {
                    if (_frames.Count >= count)
                    {
                        return;
                    }
                }

                await Task.Delay(20);
            }

            throw new TimeoutException($"{count} mesaj beklendi, gelmedi");
        }

        public ValueTask DisposeAsync()
        {
            Stop();
            _cts?.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}