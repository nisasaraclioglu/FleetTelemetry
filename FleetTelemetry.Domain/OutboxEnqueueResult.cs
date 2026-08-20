namespace FleetTelemetry.Domain;

public enum OutboxEnqueueResult
{
    Added = 0,
    AddedWithEviction
}