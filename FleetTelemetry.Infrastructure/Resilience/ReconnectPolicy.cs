using FleetTelemetry.Application.Abstractions;
using FleetTelemetry.Application.Options;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Resilience;

public sealed class ReconnectPolicy : IReconnectPolicy
{
    private readonly ReconnectOptions _options;
    private readonly Func<double> _randomSource;

    public ReconnectPolicy(IOptions<ReconnectOptions> options)
        : this(options, Random.Shared.NextDouble)
    {
    }

    internal ReconnectPolicy(IOptions<ReconnectOptions> options, Func<double> randomSource)
    {
        _options = options.Value;
        _randomSource = randomSource;
    }

    public TimeSpan GetDelay(int attempt)
    {
        if (attempt < 1)
        {
            attempt = 1;
        }

        var baseDelay = CalculateBaseDelay(attempt);
        var jitter = baseDelay * _options.JitterRatio * _randomSource();

        return TimeSpan.FromMilliseconds(baseDelay + jitter);
    }

    private double CalculateBaseDelay(int attempt)
    {
        var initial = _options.InitialDelay.TotalMilliseconds;
        var max = _options.MaxDelay.TotalMilliseconds;

        var exponent = attempt - 1;
        var multiplier = Math.Pow(_options.BackoffMultiplier, exponent);

        if (double.IsInfinity(multiplier) || initial * multiplier > max)
        {
            return max;
        }

        return initial * multiplier;
    }
}