namespace FleetTelemetry.Application.Options;

public sealed class IdleOptions
{
    public const string SectionName = "FleetTelemetry:Idle";

    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(60);

    public TimeSpan IdleThreshold { get; set; } = TimeSpan.FromDays(1);
}