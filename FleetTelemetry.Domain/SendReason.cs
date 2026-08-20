namespace FleetTelemetry.Domain;

public enum SendReason
{
    DoNotSend = 0,
    FirstSend,
    ContactChanged,
    IntervalElapsed
}