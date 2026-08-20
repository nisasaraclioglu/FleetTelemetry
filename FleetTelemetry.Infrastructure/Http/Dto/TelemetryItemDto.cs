using System.Text.Json.Serialization;

namespace FleetTelemetry.Infrastructure.Http.Dto;

public sealed class TelemetryItemDto
{
    [JsonPropertyName("deviceCode")]
    public string? DeviceCode { get; set; }

    [JsonPropertyName("deviceName")]
    public string? DeviceName { get; set; }

    [JsonPropertyName("capTime")]
    public long? CapTime { get; set; }

    [JsonPropertyName("gpsX")]
    public double? GpsX { get; set; }

    [JsonPropertyName("gpsY")]
    public double? GpsY { get; set; }

    [JsonPropertyName("speed")]
    public double? Speed { get; set; }

    [JsonPropertyName("angle")]
    public double? Angle { get; set; }

    [JsonPropertyName("direction")]
    public string? Direction { get; set; }

    [JsonPropertyName("isOnline")]
    public int? IsOnline { get; set; }
}