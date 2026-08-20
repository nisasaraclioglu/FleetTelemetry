namespace FleetTelemetry.Application.Abstractions;

public interface IReconnectPolicy
{
    TimeSpan GetDelay(int attempt);
}