namespace FleetTelemetry.Domain;

public enum ConnectionState
{
    Disconnected = 0,
    Connecting,
    Connected,
    Faulted,
    Disposed
}