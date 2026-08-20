using FleetTelemetry.Application.Abstractions;
using FleetTelemetry.Domain;
using Microsoft.Extensions.Logging;

namespace FleetTelemetry.Infrastructure.Http;

public sealed class FakeTelemetrySource : ITelemetrySource
{
    private const int DeviceCount = 20;
    private const double ContactOffRatio = 0.3;

    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FakeTelemetrySource> _logger;
    private readonly FakeDevice[] _devices;

    public FakeTelemetrySource(
        TimeProvider timeProvider,
        ILogger<FakeTelemetrySource> logger)
    {
        _timeProvider = timeProvider;
        _logger = logger;
        _devices = CreateDevices();
    }

    public Task<IReadOnlyCollection<DeviceTelemetry>> GetLatestAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var now = _timeProvider.GetUtcNow();
        var result = new List<DeviceTelemetry>(_devices.Length);

        foreach (var device in _devices)
        {
            device.Advance();
            result.Add(device.ToTelemetry(now));
        }

        _logger.LogDebug("Sahte kaynak {Count} kayıt üretti", result.Count);

        return Task.FromResult<IReadOnlyCollection<DeviceTelemetry>>(result);
    }

    private static FakeDevice[] CreateDevices()
    {
        var random = new Random(Seed: 42);
        var devices = new FakeDevice[DeviceCount];

        for (var i = 0; i < DeviceCount; i++)
        {
            devices[i] = new FakeDevice(
                deviceCode: (1000691 + i).ToString(),
                deviceName: $"34TST{100 + i}",
                latitude: 39.0 + random.NextDouble(),
                longitude: 32.0 + random.NextDouble(),
                angle: random.Next(0, 360),
                isOnline: random.NextDouble() > ContactOffRatio,
                random: random);
        }

        return devices;
    }

    private sealed class FakeDevice(
        string deviceCode,
        string deviceName,
        double latitude,
        double longitude,
        double angle,
        bool isOnline,
        Random random)
    {
        private double _latitude = latitude;
        private double _longitude = longitude;
        private double _angle = angle;
        private double _speed = isOnline ? 60 : 0;
        private bool _isOnline = isOnline;
        private int _tickCount;

        public void Advance()
        {
            _tickCount++;

            if (_tickCount % 30 == 0)
            {
                _isOnline = !_isOnline;
            }

            if (!_isOnline)
            {
                _speed = 0;
                return;
            }

            _angle = (_angle + random.Next(-10, 11) + 360) % 360;
            _speed = Math.Clamp(_speed + random.Next(-5, 6), 0, 120);

            var radians = _angle * Math.PI / 180;
            var distance = _speed / 400_000.0;

            _latitude += Math.Cos(radians) * distance;
            _longitude += Math.Sin(radians) * distance;
        }

        public DeviceTelemetry ToTelemetry(DateTimeOffset now) => new()
        {
            DeviceCode = deviceCode,
            DeviceName = deviceName,
            DataDate = now,
            GpsLat = Math.Round(_latitude, 6),
            GpsLon = Math.Round(_longitude, 6),
            Speed = Math.Round(_speed, 2),
            Angle = Math.Round(_angle, 1),
            Direction = ToDirection(_angle),
            IsOnline = _isOnline
        };

        private static TravelDirection ToDirection(double angle) =>
            (((int)Math.Round(angle / 45.0)) % 8) switch
            {
                0 => TravelDirection.North,
                1 => TravelDirection.NorthEast,
                2 => TravelDirection.East,
                3 => TravelDirection.SouthEast,
                4 => TravelDirection.South,
                5 => TravelDirection.SouthWest,
                6 => TravelDirection.West,
                _ => TravelDirection.NorthWest
            };
    }
}