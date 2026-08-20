using FleetTelemetry.Application.Options;
using FleetTelemetry.Application.Policies;
using FleetTelemetry.Domain;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace FleetTelemetry.UnitTests;

public sealed class ContactBasedSendPolicyTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 1, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new();

    private ContactBasedSendPolicy CreateSut()
    {
        _clock.SetUtcNow(Now);
        var options = Options.Create(new SendPolicyOptions());
        return new ContactBasedSendPolicy(options, _clock);
    }

    private static DeviceTelemetry Telemetry(bool isOnline) => new()
    {
        DeviceCode = "1000691",
        DataDate = Now,
        GpsLat = 40.921852,
        GpsLon = 38.320351,
        Speed = 65.5,
        Angle = 133.0,
        IsOnline = isOnline
    };

    private static DeviceSendState State(bool contact, DateTimeOffset sentAt) => new()
    {
        LastSentAt = sentAt,
        LastKnownContact = contact,
        LastDataSeenAt = sentAt
    };

    [Fact]
    public void OncekiDurumYoksa_IlkGonderimYapilir()
    {
        var sut = CreateSut();

        var result = sut.Decide(Telemetry(isOnline: true), previousState: null);

        Assert.Equal(SendReason.FirstSend, result);
    }

    [Fact]
    public void KontakKapaliylaAcildiginda_HemenGonderilir()
    {
        var sut = CreateSut();
        var previous = State(contact: false, sentAt: Now);

        var result = sut.Decide(Telemetry(isOnline: true), previous);

        Assert.Equal(SendReason.ContactChanged, result);
    }

    [Fact]
    public void KontakAcikkenKapandiginda_HemenGonderilir()
    {
        var sut = CreateSut();
        var previous = State(contact: true, sentAt: Now);

        var result = sut.Decide(Telemetry(isOnline: false), previous);

        Assert.Equal(SendReason.ContactChanged, result);
    }

    [Fact]
    public void KontakAcikVeOnSaniyeGectiyse_Gonderilir()
    {
        var sut = CreateSut();
        var previous = State(contact: true, sentAt: Now);

        _clock.Advance(TimeSpan.FromSeconds(10));

        var result = sut.Decide(Telemetry(isOnline: true), previous);

        Assert.Equal(SendReason.IntervalElapsed, result);
    }

    [Fact]
    public void KontakAcikVeSureDolmadiysa_Gonderilmez()
    {
        var sut = CreateSut();
        var previous = State(contact: true, sentAt: Now);

        _clock.Advance(TimeSpan.FromSeconds(5));

        var result = sut.Decide(Telemetry(isOnline: true), previous);

        Assert.Equal(SendReason.DoNotSend, result);
    }

    [Fact]
    public void KontakKapaliVeOnDakikaGectiyse_Gonderilir()
    {
        var sut = CreateSut();
        var previous = State(contact: false, sentAt: Now);

        _clock.Advance(TimeSpan.FromMinutes(10));

        var result = sut.Decide(Telemetry(isOnline: false), previous);

        Assert.Equal(SendReason.IntervalElapsed, result);
    }

    [Fact]
    public void KontakKapaliVeOnSaniyeGectiyse_Gonderilmez()
    {
        var sut = CreateSut();
        var previous = State(contact: false, sentAt: Now);

        _clock.Advance(TimeSpan.FromSeconds(10));

        var result = sut.Decide(Telemetry(isOnline: false), previous);

        Assert.Equal(SendReason.DoNotSend, result);
    }

    [Fact]
    public void TurGecikmesiToleransIcindeyse_Gonderilir()
    {
        var sut = CreateSut();
        var previous = State(contact: true, sentAt: Now);

        _clock.Advance(TimeSpan.FromMilliseconds(9_500));

        var result = sut.Decide(Telemetry(isOnline: true), previous);

        Assert.Equal(SendReason.IntervalElapsed, result);
    }
}