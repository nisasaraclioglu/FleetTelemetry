namespace FleetTelemetry.Domain;

public sealed record DeviceSendState
{
    public required DateTimeOffset LastSentAt { get; init; }

    public required bool LastKnownContact { get; init; }

    public required DateTimeOffset LastDataSeenAt { get; init; }

    public DeviceSendState WithSent(DateTimeOffset sentAt, bool contact) =>
        this with
        {
            LastSentAt = sentAt,
            LastKnownContact = contact,
            LastDataSeenAt = sentAt
        };

    public DeviceSendState WithDataSeen(DateTimeOffset seenAt) =>
        this with { LastDataSeenAt = seenAt };
}