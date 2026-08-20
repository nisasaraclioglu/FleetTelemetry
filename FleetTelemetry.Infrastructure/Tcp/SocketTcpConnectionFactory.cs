using FleetTelemetry.Application.Abstractions;
using FleetTelemetry.Application.Options;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Tcp;

public sealed class SocketTcpConnectionFactory : ITcpConnectionFactory
{
    private readonly TcpOptions _options;

    public SocketTcpConnectionFactory(IOptions<TcpOptions> options)
    {
        _options = options.Value;
    }

    public ITcpConnection Create() => new SocketTcpConnection(_options.NoDelay);
}