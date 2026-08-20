using FleetTelemetry.Domain;
using FleetTelemetry.Infrastructure.Http;
using FleetTelemetry.Infrastructure.Http.Dto;

namespace FleetTelemetry.UnitTests;

public sealed class TelemetryDtoMapperTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 1, 16, 16, 40, 0, TimeSpan.Zero);

    private const long ValidCapTime = 1768570371;

    private static TelemetryItemDto ValidDto() => new()
    {
        DeviceCode = "1000691",
        DeviceName = "34NKZ689",
        CapTime = ValidCapTime,
        GpsX = 38.320351,
        GpsY = 40.921852,
        Speed = 65.577469,
        Angle = 133.0,
        Direction = "SouthEast",
        IsOnline = 1
    };

    [Fact]
    public void GecerliKayit_ModeleDonusur()
    {
        var result = TelemetryDtoMapper.Map(ValidDto(), Now);

        Assert.True(result.IsSuccess);

        var value = result.Value!;
        Assert.Equal("1000691", value.DeviceCode);
        Assert.Equal("34NKZ689", value.DeviceName);
        Assert.Equal(40.921852, value.GpsLat);
        Assert.Equal(38.320351, value.GpsLon);
        Assert.Equal(65.577469, value.Speed);
        Assert.Equal(133.0, value.Angle);
        Assert.Equal(TravelDirection.SouthEast, value.Direction);
        Assert.True(value.IsOnline);
    }

    [Fact]
    public void CapTime_UtcTarihineDonusur()
    {
        var result = TelemetryDtoMapper.Map(ValidDto(), Now);

        Assert.Equal(
            DateTimeOffset.FromUnixTimeSeconds(ValidCapTime),
            result.Value!.DataDate);
        Assert.Equal(TimeSpan.Zero, result.Value!.DataDate.Offset);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DeviceCodeBossa_Reddedilir(string? deviceCode)
    {
        var dto = ValidDto();
        dto.DeviceCode = deviceCode;

        var result = TelemetryDtoMapper.Map(dto, Now);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void CapTimeYoksa_Reddedilir()
    {
        var dto = ValidDto();
        dto.CapTime = null;

        Assert.False(TelemetryDtoMapper.Map(dto, Now).IsSuccess);
    }

    [Fact]
    public void GelecekTarihliKayit_Reddedilir()
    {
        var dto = ValidDto();
        dto.CapTime = Now.AddMinutes(10).ToUnixTimeSeconds();

        Assert.False(TelemetryDtoMapper.Map(dto, Now).IsSuccess);
    }

    [Fact]
    public void KucukIleriSapma_KabulEdilir()
    {
        var dto = ValidDto();
        dto.CapTime = Now.AddMinutes(2).ToUnixTimeSeconds();

        Assert.True(TelemetryDtoMapper.Map(dto, Now).IsSuccess);
    }

    [Fact]
    public void CokEskiKayit_Reddedilir()
    {
        var dto = ValidDto();
        dto.CapTime = Now.AddHours(-30).ToUnixTimeSeconds();

        Assert.False(TelemetryDtoMapper.Map(dto, Now).IsSuccess);
    }

    [Theory]
    [InlineData(null, 38.3)]
    [InlineData(91.0, 38.3)]
    [InlineData(-91.0, 38.3)]
    public void GecersizEnlem_Reddedilir(double? gpsY, double gpsX)
    {
        var dto = ValidDto();
        dto.GpsY = gpsY;
        dto.GpsX = gpsX;

        Assert.False(TelemetryDtoMapper.Map(dto, Now).IsSuccess);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(181.0)]
    [InlineData(-181.0)]
    public void GecersizBoylam_Reddedilir(double? gpsX)
    {
        var dto = ValidDto();
        dto.GpsX = gpsX;

        Assert.False(TelemetryDtoMapper.Map(dto, Now).IsSuccess);
    }

    [Theory]
    [InlineData(-5.0, 0.0)]
    [InlineData(null, 0.0)]
    [InlineData(80.5, 80.5)]
    public void Hiz_NegatifVeBosDegerlerdeSifirlanir(double? input, double expected)
    {
        var dto = ValidDto();
        dto.Speed = input;

        Assert.Equal(expected, TelemetryDtoMapper.Map(dto, Now).Value!.Speed);
    }

    [Theory]
    [InlineData(133.0, 133.0)]
    [InlineData(360.0, 0.0)]
    [InlineData(370.0, 10.0)]
    [InlineData(-90.0, 270.0)]
    [InlineData(null, 0.0)]
    public void Aci_SifirUcYuzAltmisAraliginaNormalizeEdilir(double? input, double expected)
    {
        var dto = ValidDto();
        dto.Angle = input;

        Assert.Equal(expected, TelemetryDtoMapper.Map(dto, Now).Value!.Angle);
    }

    [Theory]
    [InlineData("SouthEast", TravelDirection.SouthEast)]
    [InlineData("southeast", TravelDirection.SouthEast)]
    [InlineData("North", TravelDirection.North)]
    [InlineData("Kuzey", TravelDirection.Unknown)]
    [InlineData("", TravelDirection.Unknown)]
    [InlineData(null, TravelDirection.Unknown)]
    [InlineData("7", TravelDirection.Unknown)]
    [InlineData("999", TravelDirection.Unknown)]
    public void Yon_TaninmayanDegerlerdeUnknownOlur(string? input, TravelDirection expected)
    {
        var dto = ValidDto();
        dto.Direction = input;

        Assert.Equal(expected, TelemetryDtoMapper.Map(dto, Now).Value!.Direction);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(null, false)]
    [InlineData(2, false)]
    public void Kontak_YalnizcaBirDegerindeAcikSayilir(int? input, bool expected)
    {
        var dto = ValidDto();
        dto.IsOnline = input;

        Assert.Equal(expected, TelemetryDtoMapper.Map(dto, Now).Value!.IsOnline);
    }

    [Fact]
    public void BosDeviceName_NullOlur()
    {
        var dto = ValidDto();
        dto.DeviceName = "   ";

        Assert.Null(TelemetryDtoMapper.Map(dto, Now).Value!.DeviceName);
    }

    [Fact]
    public void BoslukluDeviceCode_Kirpilir()
    {
        var dto = ValidDto();
        dto.DeviceCode = "  1000691  ";

        Assert.Equal("1000691", TelemetryDtoMapper.Map(dto, Now).Value!.DeviceCode);
    }
}