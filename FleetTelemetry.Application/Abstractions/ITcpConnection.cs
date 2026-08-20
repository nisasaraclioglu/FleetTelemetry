namespace FleetTelemetry.Application.Abstractions;

public interface ITcpConnection : IAsyncDisposable
{
    Task ConnectAsync(string host, int port, CancellationToken ct);

    Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct);
}