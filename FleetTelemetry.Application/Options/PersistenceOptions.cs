namespace FleetTelemetry.Application.Options;

public sealed class PersistenceOptions
{
    public const string SectionName = "FleetTelemetry:Persistence";

    public TimeSpan SnapshotInterval { get; set; } = TimeSpan.FromSeconds(30);

    public bool DatabaseEnabled { get; set; }

    public string FileFallbackDirectory { get; set; } = "outbox";

    public bool SnapshotOnShutdown { get; set; } = true;

    public string ConnectionString { get; set; } = string.Empty;

    public string TableName { get; set; } = "dbo.OutboxTelemetry";
}