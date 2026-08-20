using FleetTelemetry.Domain;
using FleetTelemetry.Infrastructure.Http.Dto;

namespace FleetTelemetry.Infrastructure.Http;

public static class TelemetryDtoMapper
{
    private const double MinLatitude = -90;
    private const double MaxLatitude = 90;
    private const double MinLongitude = -180;
    private const double MaxLongitude = 180;

    public static MappingResult Map(TelemetryItemDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.DeviceCode))
        {
            return MappingResult.Failure("deviceCode boş");
        }

        if (dto.CapTime is not { } capTime)
        {
            return MappingResult.Failure("capTime yok");
        }

        var dataDate = DateTimeOffset.FromUnixTimeSeconds(capTime);

        var latitude = dto.GpsY;
        var longitude = dto.GpsX;

        if (latitude is not { } lat || lat is < MinLatitude or > MaxLatitude)
        {
            return MappingResult.Failure("enlem geçersiz");
        }

        if (longitude is not { } lon || lon is < MinLongitude or > MaxLongitude)
        {
            return MappingResult.Failure("boylam geçersiz");
        }

        return MappingResult.Success(new DeviceTelemetry
        {
            DeviceCode = dto.DeviceCode.Trim(),
            DeviceName = string.IsNullOrWhiteSpace(dto.DeviceName)
                ? null
                : dto.DeviceName.Trim(),
            DataDate = dataDate,
            GpsLat = lat,
            GpsLon = lon,
            Speed = NormalizeSpeed(dto.Speed),
            Angle = NormalizeAngle(dto.Angle),
            Direction = ParseDirection(dto.Direction),
            IsOnline = dto.IsOnline == 1
        });
    }

    private static double NormalizeSpeed(double? speed) =>
        speed is not { } value || double.IsNaN(value) || value < 0
            ? 0
            : value;

    private static double NormalizeAngle(double? angle)
    {
        if (angle is not { } value || double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        var normalized = value % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private static TravelDirection ParseDirection(string? direction)
    {
        if (string.IsNullOrWhiteSpace(direction))
        {
            return TravelDirection.Unknown;
        }

        if (!char.IsLetter(direction[0]))
        {
            return TravelDirection.Unknown;
        }

        return Enum.TryParse<TravelDirection>(direction, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed)
                ? parsed
                : TravelDirection.Unknown;
    }
}