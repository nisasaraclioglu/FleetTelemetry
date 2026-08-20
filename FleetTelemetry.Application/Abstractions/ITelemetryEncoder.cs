using FleetTelemetry.Domain;

namespace FleetTelemetry.Application.Abstractions;

public interface ITelemetryEncoder
{
    ReadOnlyMemory<byte> Encode(DeviceTelemetry telemetry);
}