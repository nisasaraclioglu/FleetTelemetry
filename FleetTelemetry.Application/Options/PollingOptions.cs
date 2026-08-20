namespace FleetTelemetry.Application.Options;

public sealed class PollingOptions
{
    public const string SectionName = "FleetTelemetry:Polling";

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan SourceTimeout { get; set; } = TimeSpan.FromSeconds(8);

    public int MaxDegreeOfParallelism { get; set; } = 4;

    /// <summary>
    /// Bir turda tek bir cihaz için gönderilecek azami kayıt sayısı.
    /// Birikmiş kuyrukların tek turda boşaltılıp tur süresini şişirmesini engeller.
    /// </summary>
    public int MaxMessagesPerDevicePerTick { get; set; } = 10;

    /// <summary>
    /// Aynı cihaza ait ardışık gönderimler arasındaki bekleme.
    /// Varsayılan sıfırdır; sunucu tarafı bir hız sınırı bildirilirse artırılır.
    /// </summary>
    public TimeSpan DelayBetweenMessages { get; set; } = TimeSpan.Zero;
}