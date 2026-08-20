using Dapper;
using FleetTelemetry.Application.Abstractions;
using FleetTelemetry.Application.Options;
using FleetTelemetry.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Outbox;

public sealed class DatabaseOutboxStore : IOutboxStore
{
    private const int CommandTimeoutSeconds = 30;
    private readonly PersistenceOptions _options;

    public DatabaseOutboxStore(IOptions<PersistenceOptions> options)
    {
        _options = options.Value;
    }

    public async Task SaveAsync(
        string deviceCode,
        IReadOnlyList<DeviceTelemetry> items,
        CancellationToken ct)
    {
        await using var connection = new SqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct);

        await using var transaction = await connection.BeginTransactionAsync(ct);

        var deleteSql = $"DELETE FROM {_options.TableName} WHERE DeviceCode = @DeviceCode;";

        await connection.ExecuteAsync(new CommandDefinition(
            deleteSql,
            new { DeviceCode = deviceCode },
            transaction,
            commandTimeout: CommandTimeoutSeconds,
            cancellationToken: ct));

        if (items.Count > 0)
        {
            var insertSql = $"""
                INSERT INTO {_options.TableName}
                    (DeviceCode, DataDate, DeviceName, GpsLat, GpsLon, Speed, Angle, Direction, IsOnline)
                VALUES
                    (@DeviceCode, @DataDate, @DeviceName, @GpsLat, @GpsLon, @Speed, @Angle, @Direction, @IsOnline);
                """;

            var rows = items.Select(x => new
            {
                x.DeviceCode,
                x.DataDate,
                x.DeviceName,
                x.GpsLat,
                x.GpsLon,
                x.Speed,
                x.Angle,
                Direction = (byte)x.Direction,
                x.IsOnline
            });

            await connection.ExecuteAsync(new CommandDefinition(
                insertSql,
                rows,
                transaction,
                commandTimeout: CommandTimeoutSeconds,
                cancellationToken: ct));
        }

        await transaction.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<DeviceTelemetry>> LoadAsync(
        string deviceCode,
        CancellationToken ct)
    {
        var sql = $"""
            SELECT DeviceCode, DataDate, DeviceName, GpsLat, GpsLon, Speed, Angle, Direction, IsOnline
            FROM {_options.TableName}
            WHERE DeviceCode = @DeviceCode
            ORDER BY DataDate;
            """;

        await using var connection = new SqlConnection(_options.ConnectionString);

        var rows = await connection.QueryAsync<OutboxRow>(new CommandDefinition(
            sql,
            new { DeviceCode = deviceCode },
            commandTimeout: CommandTimeoutSeconds,
            cancellationToken: ct));

        return rows.Select(x => x.ToTelemetry()).ToArray();
    }

    public async Task<IReadOnlyCollection<string>> ListDeviceCodesAsync(CancellationToken ct)
    {
        var sql = $"""
            SELECT DeviceCode
            FROM {_options.TableName}
            GROUP BY DeviceCode;
            """;

        await using var connection = new SqlConnection(_options.ConnectionString);

        var codes = await connection.QueryAsync<string>(
            new CommandDefinition(sql, commandTimeout: CommandTimeoutSeconds, cancellationToken: ct));

        return codes.ToArray();
    }

    public async Task ClearAsync(string deviceCode, CancellationToken ct)
    {
        var sql = $"DELETE FROM {_options.TableName} WHERE DeviceCode = @DeviceCode;";

        await using var connection = new SqlConnection(_options.ConnectionString);

        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { DeviceCode = deviceCode },
            commandTimeout: CommandTimeoutSeconds,
            cancellationToken: ct));
    }

    private sealed class OutboxRow
    {
        public string DeviceCode { get; init; } = string.Empty;
        public DateTimeOffset DataDate { get; init; }
        public string? DeviceName { get; init; }
        public double GpsLat { get; init; }
        public double GpsLon { get; init; }
        public double Speed { get; init; }
        public double Angle { get; init; }
        public byte Direction { get; init; }
        public bool IsOnline { get; init; }

        public DeviceTelemetry ToTelemetry() => new()
        {
            DeviceCode = DeviceCode,
            DataDate = DataDate,
            DeviceName = DeviceName,
            GpsLat = GpsLat,
            GpsLon = GpsLon,
            Speed = Speed,
            Angle = Angle,
            Direction = (TravelDirection)Direction,
            IsOnline = IsOnline
        };
    }
}