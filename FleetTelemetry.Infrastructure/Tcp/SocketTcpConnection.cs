using System.Net.Sockets;
using FleetTelemetry.Application.Abstractions;

namespace FleetTelemetry.Infrastructure.Tcp;

public sealed class SocketTcpConnection : ITcpConnection
{
    private readonly TcpClient _client;
    private NetworkStream? _stream;

    public SocketTcpConnection(bool noDelay)
    {
        _client = new TcpClient
        {
            NoDelay = noDelay
        };
    }

    public async Task ConnectAsync(string host, int port, CancellationToken ct)
    {
        await _client.ConnectAsync(host, port, ct);
        _stream = _client.GetStream();
    }

    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        if (_stream is null)
        {
            throw new InvalidOperationException("Bağlantı kurulmadan gönderim yapılamaz");
        }

        await _stream.WriteAsync(data, ct);
        await _stream.FlushAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_stream is not null)
        {
            await _stream.DisposeAsync();
        }

        _client.Dispose();
    }
}