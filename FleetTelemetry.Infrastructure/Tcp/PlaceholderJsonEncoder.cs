using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using FleetTelemetry.Application.Abstractions;
using FleetTelemetry.Application.Options;
using FleetTelemetry.Domain;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Tcp;

/// <summary>
/// STR protokol spesifikasyonu sağlanmadığı için kullanılan geçici encoder.
/// 4 byte big-endian uzunluk öneki + UTF-8 JSON gövde üretir.
/// Spesifikasyon geldiğinde bu sınıfın yerine StrTelemetryEncoder yazılacaktır.
/// Başka hiçbir sınıf etkilenmeyecektir.
/// </summary>
public sealed class PlaceholderJsonEncoder : ITelemetryEncoder
{
    private const int LengthPrefixSize = 4;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private readonly TimeSpan _targetOffset;

    public PlaceholderJsonEncoder(IOptions<TimeZoneOptions> options)
    {
        _targetOffset = TimeSpan.FromHours(options.Value.TargetUtcOffsetHours);
    }

    public ReadOnlyMemory<byte> Encode(DeviceTelemetry telemetry)
    {
        var payload = new
        {
            deviceCode = telemetry.DeviceCode,
            deviceName = telemetry.DeviceName,
            dataDate = telemetry.DataDate
                .ToOffset(_targetOffset)
                .ToString("yyyy-MM-dd HH:mm:ss"),
            gpsLat = telemetry.GpsLat,
            gpsLon = telemetry.GpsLon,
            speed = telemetry.Speed,
            angle = telemetry.Angle,
            direction = telemetry.Direction.ToString(),
            isOnline = telemetry.IsOnline ? 1 : 0
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var body = Encoding.UTF8.GetBytes(json);

        var buffer = new byte[LengthPrefixSize + body.Length];

        BinaryPrimitives.WriteInt32BigEndian(buffer, body.Length);
        body.CopyTo(buffer, LengthPrefixSize);

        return buffer;
    }
}