using FleetTelemetry.Application.Options;
using FleetTelemetry.Infrastructure.Resilience;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.UnitTests;

public sealed class ReconnectPolicyTests
{
    private static ReconnectPolicy CreateSut(
        double randomValue = 0.0,
        double jitterRatio = 0.3,
        double maxSeconds = 30)
    {
        var options = Options.Create(new ReconnectOptions
        {
            InitialDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(maxSeconds),
            BackoffMultiplier = 2.0,
            JitterRatio = jitterRatio
        });

        return new ReconnectPolicy(options, () => randomValue);
    }

    [Theory]
    [InlineData(1, 1000)]
    [InlineData(2, 2000)]
    [InlineData(3, 4000)]
    [InlineData(4, 8000)]
    [InlineData(5, 16000)]
    public void JitterYokken_SureUstelArtar(int attempt, double expectedMs)
    {
        var sut = CreateSut(randomValue: 0.0);

        var delay = sut.GetDelay(attempt);

        Assert.Equal(expectedMs, delay.TotalMilliseconds);
    }

    [Fact]
    public void TavanaUlasinca_SureSabitKalir()
    {
        var sut = CreateSut(randomValue: 0.0);

        Assert.Equal(30_000, sut.GetDelay(6).TotalMilliseconds);
        Assert.Equal(30_000, sut.GetDelay(7).TotalMilliseconds);
        Assert.Equal(30_000, sut.GetDelay(50).TotalMilliseconds);
    }

    [Fact]
    public void CokBuyukDenemeSayisi_TasmaYapmaz()
    {
        var sut = CreateSut(randomValue: 0.0);

        var delay = sut.GetDelay(5000);

        Assert.Equal(30_000, delay.TotalMilliseconds);
    }

    [Fact]
    public void JitterTamOraninda_SureyiBeklenenKadarArtirir()
    {
        var sut = CreateSut(randomValue: 1.0, jitterRatio: 0.3);

        var delay = sut.GetDelay(3);

        Assert.Equal(5200, delay.TotalMilliseconds);
    }

    [Fact]
    public void JitterYarisinda_SureyiYarimOranindaArtirir()
    {
        var sut = CreateSut(randomValue: 0.5, jitterRatio: 0.3);

        var delay = sut.GetDelay(1);

        Assert.Equal(1150, delay.TotalMilliseconds);
    }

    [Fact]
    public void JitterKapaliysa_SureTamOlarakTemelDegerdir()
    {
        var sut = CreateSut(randomValue: 1.0, jitterRatio: 0.0);

        Assert.Equal(4000, sut.GetDelay(3).TotalMilliseconds);
    }

    [Fact]
    public void SureAsagiDogruSacilmaz()
    {
        var sut = CreateSut(randomValue: 0.0);

        var delay = sut.GetDelay(2);

        Assert.True(delay.TotalMilliseconds >= 2000);
    }

    [Fact]
    public void GecersizDenemeSayisi_IlkDenemeGibiDavranir()
    {
        var sut = CreateSut(randomValue: 0.0);

        Assert.Equal(1000, sut.GetDelay(0).TotalMilliseconds);
        Assert.Equal(1000, sut.GetDelay(-5).TotalMilliseconds);
    }

    [Fact]
    public void GercekRastgelelikle_SureBeklenenAraliktaKalir()
    {
        var options = Options.Create(new ReconnectOptions
        {
            InitialDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(30),
            BackoffMultiplier = 2.0,
            JitterRatio = 0.3
        });

        var sut = new ReconnectPolicy(options);

        for (var i = 0; i < 200; i++)
        {
            var delay = sut.GetDelay(3);

            Assert.InRange(delay.TotalMilliseconds, 4000, 5200);
        }
    }
}