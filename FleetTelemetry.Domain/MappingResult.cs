namespace FleetTelemetry.Domain;

public readonly record struct MappingResult
{
    private MappingResult(DeviceTelemetry? value, string? error)
    {
        Value = value;
        Error = error;
    }

    public DeviceTelemetry? Value { get; }

    public string? Error { get; }

    public bool IsSuccess => Value is not null;

    public static MappingResult Success(DeviceTelemetry value) => new(value, null);

    public static MappingResult Failure(string error) => new(null, error);
}