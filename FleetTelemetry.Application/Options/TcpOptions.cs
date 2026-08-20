namespace FleetTelemetry.Application.Options;

public sealed class TcpOptions
{
    public const string SectionName = "FleetTelemetry:Tcp";

    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 9100;

    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan SendTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public bool NoDelay { get; set; } = true;
}