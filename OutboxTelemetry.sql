IF OBJECT_ID('dbo.OutboxTelemetry', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OutboxTelemetry
    (
        DeviceCode  VARCHAR(50)       NOT NULL,
        DataDate    DATETIMEOFFSET(3) NOT NULL,
        DeviceName  NVARCHAR(50)      NULL,
        GpsLat      FLOAT             NOT NULL,
        GpsLon      FLOAT             NOT NULL,
        Speed       FLOAT             NOT NULL,
        Angle       FLOAT             NOT NULL,
        Direction   TINYINT           NOT NULL,
        IsOnline    BIT               NOT NULL,
        CONSTRAINT PK_OutboxTelemetry PRIMARY KEY CLUSTERED (DeviceCode, DataDate)
    );
END