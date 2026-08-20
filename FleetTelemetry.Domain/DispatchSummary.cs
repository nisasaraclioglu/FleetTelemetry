namespace FleetTelemetry.Domain;

public sealed record DispatchSummary
{
    public int Total { get; init; }
    public int Sent { get; init; }
    public int SkippedByPolicy { get; init; }
    public int SkippedBackoff { get; init; }
    public int Queued { get; init; }
    public int Failed { get; init; }
    public long ElapsedMs { get; init; }
}