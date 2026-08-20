namespace FleetTelemetry.Application.Options;

public sealed class PollingOptions
{
    public const string SectionName = "FleetTelemetry:Polling";

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan SourceTimeout { get; set; } = TimeSpan.FromSeconds(8);

    public int MaxDegreeOfParallelism { get; set; } = 4;
}