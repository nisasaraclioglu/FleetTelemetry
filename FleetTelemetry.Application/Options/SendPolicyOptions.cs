namespace FleetTelemetry.Application.Options;

public sealed class SendPolicyOptions
{
    public const string SectionName = "FleetTelemetry:SendPolicy";

    public TimeSpan ContactOnInterval { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan ContactOffInterval { get; set; } = TimeSpan.FromMinutes(10);

    public bool SendOnContactChange { get; set; } = true;

    public TimeSpan Tolerance { get; set; } = TimeSpan.FromSeconds(1);
}