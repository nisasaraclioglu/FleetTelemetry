using FleetTelemetry.Domain;

namespace FleetTelemetry.Application.Abstractions;

public interface ISendDecisionPolicy
{
    SendReason Decide(DeviceTelemetry telemetry, DeviceSendState? previousState);
}