namespace FleetTelemetry.Domain;

public sealed record DeviceTelemetry
{
    public required string DeviceCode { get; init; }

    public string? DeviceName { get; init; }

    public required DateTimeOffset DataDate { get; init; }

    public required double GpsLat { get; init; }

    public required double GpsLon { get; init; }

    public required double Speed { get; init; }

    public required double Angle { get; init; }

    public TravelDirection Direction { get; init; } = TravelDirection.Unknown;

    public required bool IsOnline { get; init; }
}