namespace FleetTelemetry.Application.Options;

public sealed class OutboxOptions
{
    public const string SectionName = "FleetTelemetry:Outbox";

    public int MaxItemsPerDevice { get; set; } = 1000;
}