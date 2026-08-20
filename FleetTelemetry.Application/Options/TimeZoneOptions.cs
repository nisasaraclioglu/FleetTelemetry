namespace FleetTelemetry.Application.Options;

public sealed class TimeZoneOptions
{
    public const string SectionName = "FleetTelemetry:TimeZone";

    public int TargetUtcOffsetHours { get; set; } = 3;
}