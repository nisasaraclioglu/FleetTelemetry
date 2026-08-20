using FleetTelemetry.Application.Abstractions;
using FleetTelemetry.Application.Options;
using FleetTelemetry.Domain;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Application.Policies;

public sealed class ContactBasedSendPolicy : ISendDecisionPolicy
{
    private readonly SendPolicyOptions _options;
    private readonly TimeProvider _timeProvider;

    public ContactBasedSendPolicy(
        IOptions<SendPolicyOptions> options,
        TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public SendReason Decide(DeviceTelemetry telemetry, DeviceSendState? previousState)
    {
        if (previousState is null)
        {
            return SendReason.FirstSend;
        }

        if (_options.SendOnContactChange &&
            telemetry.IsOnline != previousState.LastKnownContact)
        {
            return SendReason.ContactChanged;
        }

        var interval = telemetry.IsOnline
            ? _options.ContactOnInterval
            : _options.ContactOffInterval;

        var elapsed = _timeProvider.GetUtcNow() - previousState.LastSentAt;

        return elapsed >= interval - _options.Tolerance
            ? SendReason.IntervalElapsed
            : SendReason.DoNotSend;
    }
}