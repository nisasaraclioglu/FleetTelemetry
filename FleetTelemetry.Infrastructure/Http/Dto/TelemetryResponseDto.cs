using System.Text.Json.Serialization;

namespace FleetTelemetry.Infrastructure.Http.Dto;

public sealed class TelemetryResponseDto
{
    [JsonPropertyName("statusCode")]
    public int StatusCode { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("result")]
    public List<TelemetryItemDto>? Result { get; set; }
}