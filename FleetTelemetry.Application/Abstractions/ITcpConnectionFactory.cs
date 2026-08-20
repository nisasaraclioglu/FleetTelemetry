namespace FleetTelemetry.Application.Abstractions;

public interface ITcpConnectionFactory
{
    ITcpConnection Create();
}