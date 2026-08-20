namespace FleetTelemetry.Application.Options;

public sealed class ReconnectOptions
{
    public const string SectionName = "FleetTelemetry:Reconnect";

    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    public double BackoffMultiplier { get; set; } = 2.0;

    public double JitterRatio { get; set; } = 0.3;
}