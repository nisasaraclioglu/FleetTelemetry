namespace FleetTelemetry.Domain;

public enum SendOutcome
{
    Sent = 0,
    SkippedByPolicy,
    SkippedBackoff,
    Queued,
    ConnectFailed,
    SendFailed,
    Disposed
}