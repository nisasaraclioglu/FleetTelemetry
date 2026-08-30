# FleetTelemetry

Araç takip cihazlarından gelen konum verilerini, cihaz bazlı kalıcı TCP bağlantıları
üzerinden hedef sunucuya ileten .NET kütüphanesi.

Mevcut bir uygulamaya entegre edilmek üzere tasarlanmıştır; bağımsız bir servis değildir.

---

## İçindekiler

1. [Ne yapar](#1-ne-yapar)
2. [Gereksinimler](#2-gereksinimler)
3. [Kullanılan teknolojiler](#3-kullanılan-teknolojiler)
4. [Proje yapısı](#4-proje-yapısı)
5. [Kurulum](#5-kurulum)
6. [Yapılandırma](#6-yapılandırma)
7. [Veritabanı kurulumu](#7-veritabanı-kurulumu)
8. [Yayınlama gereksinimleri](#8-yayınlama-gereksinimleri)
9. [Çalışma mantığı](#9-çalışma-mantığı)
10. [Loglar ve izleme](#10-loglar-ve-izleme)
11. [Sorun giderme](#11-sorun-giderme)
12. [Geliştirme ortamı](#12-geliştirme-ortamı)
13. [Bilinen sınırlar](#13-bilinen-sınırlar)

---

## 1. Ne yapar

- Bir veri kaynağından araç telemetrisi okur (konum, hız, açı, yön, kontak durumu)
- Her cihaz için ayrı bir TCP bağlantısı açar ve uygulama ömrü boyunca açık tutar
- Kontak durumuna göre değişen aralıklarla veri gönderir
- Bağlantı koptuğunda kademeli aralıklarla yeniden bağlanmayı dener
- Gönderilemeyen verileri kuyrukta tutar, veritabanına ve gerektiğinde diske kaydeder
- Uygulama yeniden başladığında kaydedilmiş verileri geri yükler
- Uzun süre veri göndermeyen cihazların bağlantılarını kapatır

---

## 2. Gereksinimler

| Gereksinim | Sürüm / Not |
|-----------|-------------|
| .NET | 10.0 veya üzeri |
| SQL Server | 2019 veya üzeri (kuyruk kaydı için) |
| Kalıcı disk alanı | En az 2 GB (yedek kayıt yolu için) |
| Hedef TCP sunucusu | IP ve port bilgisi gereklidir |

Visual Studio ile geliştirme yapılacaksa 2026 (18.x) sürümü gerekir; .NET 10 hedefi
daha eski sürümlerde desteklenmez.

---

## 3. Kullanılan teknolojiler

| Amaç | Kullanılan |
|------|-----------|
| Çalışma zamanı | .NET 10, C# |
| Barındırma ve arka plan servisleri | `Microsoft.Extensions.Hosting` |
| Bağımlılık yönetimi | `Microsoft.Extensions.DependencyInjection` |
| Yapılandırma | `Microsoft.Extensions.Options` |
| Zaman yönetimi | `TimeProvider` (framework) |
| Loglama | `Microsoft.Extensions.Logging.Abstractions` |
| Veritabanı erişimi | Dapper + `Microsoft.Data.SqlClient` |
| Ağ | `System.Net.Sockets.TcpClient` |
| Serileştirme | `System.Text.Json` |
| Test | xUnit, `Microsoft.Extensions.TimeProvider.Testing` |

**Loglama notu:** Kütüphane belirli bir log sağlayıcısına bağlı değildir. Yalnızca
`ILogger` soyutlamasını kullanır; loglar ana uygulamanızın mevcut log altyapısına akar.

---

## 4. Proje yapısı

```
FleetTelemetry.Domain           Veri modelleri ve sabit değerler. Dış bağımlılığı yok.
FleetTelemetry.Application      Arayüzler, iş kuralları, arka plan servisleri.
FleetTelemetry.Infrastructure   TCP, veritabanı, dosya ve HTTP gerçeklemeleri.
FleetTelemetry.Host             Geliştirme sırasında test için kullanılır.
FleetTelemetry.UnitTests        Hızlı birim testleri (ağ ve dosya kullanmaz).
FleetTelemetry.IntegrationTests Gerçek soket kullanan testler.
db/OutboxTelemetry.sql          Veritabanı tablo şeması.
```

Katmanlar arası bağımlılık yönü tek yönlüdür:
`Host → Infrastructure → Application → Domain`

---

## 5. Kurulum

### 5.1 Projeyi referans olarak ekleyin

Domain, Application ve Infrastructure projelerini çözümünüze ekleyip ana uygulamanızdan
Infrastructure projesine referans verin.

### 5.2 Servisleri kaydedin

Uygulamanızın servis kayıt bölümüne tek satır ekleyin:

```csharp
services.AddFleetTelemetry(configuration);
```

Bu çağrı tüm bileşenleri, zamanlayıcıları ve arka plan servislerini kaydeder. Başka bir
kayıt işlemi gerekmez.

### 5.3 Yapılandırmayı ekleyin

`appsettings.json` dosyanıza `FleetTelemetry` bölümünü ekleyin (Bölüm 6).

### 5.4 Veritabanı tablosunu oluşturun

`db/OutboxTelemetry.sql` betiğini çalıştırın (Bölüm 7).

---

## 6. Yapılandırma

Tüm ayarlar `appsettings.json` dosyasındaki `FleetTelemetry` bölümünden yapılır.
Kod içinde sabit değer bulunmaz.

### 6.1 Tam yapılandırma örneği

```json
{
  "FleetTelemetry": {
    "Polling": {
      "Interval": "00:00:10",
      "SourceTimeout": "00:00:08",
      "MaxDegreeOfParallelism": 4,
      "MaxMessagesPerDevicePerTick": 2,
      "DelayBetweenMessages": "00:00:04"
    },
    "Tcp": {
      "Host": "10.0.0.50",
      "Port": 9100,
      "ConnectTimeout": "00:00:05",
      "SendTimeout": "00:00:05",
      "NoDelay": true
    },
    "Reconnect": {
      "InitialDelay": "00:00:01",
      "MaxDelay": "00:00:30",
      "BackoffMultiplier": 2.0,
      "JitterRatio": 0.3
    },
    "Outbox": {
      "MaxItemsPerDevice": 1000
    },
    "Persistence": {
      "SnapshotInterval": "00:00:30",
      "DatabaseEnabled": true,
      "ConnectionString": "Server=...;Database=...;",
      "TableName": "dbo.OutboxTelemetry",
      "FileFallbackDirectory": "/var/lib/fleet-telemetry/outbox",
      "SnapshotOnShutdown": true
    },
    "SendPolicy": {
      "ContactOnInterval": "00:00:10",
      "ContactOffInterval": "00:10:00",
      "SendOnContactChange": true,
      "Tolerance": "00:00:01"
    },
    "Idle": {
      "SweepInterval": "01:00:00",
      "IdleThreshold": "1.00:00:00"
    },
    "TimeZone": {
      "TargetUtcOffsetHours": 3
    }
  }
}
```

Süre değerleri `saat:dakika:saniye` biçimindedir. Bir günden uzun süreler için
`gün.saat:dakika:saniye` kullanılır (`1.00:00:00` = bir gün).

### 6.2 Mutlaka doldurulması gereken ayarlar

| Ayar | Açıklama |
|------|----------|
| `Tcp:Host` | Hedef TCP sunucusunun IP adresi |
| `Tcp:Port` | Hedef TCP sunucusunun portu |
| `Persistence:ConnectionString` | SQL Server bağlantı dizesi |
| `Persistence:FileFallbackDirectory` | Kalıcı disk alanının yolu |

### 6.3 Ayar açıklamaları

**Polling — veri okuma ve dağıtım**

| Ayar | Varsayılan | Açıklama |
|------|-----------|----------|
| `Interval` | 10 sn | Veri kaynağının kaç saniyede bir okunacağı |
| `SourceTimeout` | 8 sn | Veri kaynağı yanıt vermezse beklenecek azami süre |
| `MaxDegreeOfParallelism` | 4 | Aynı anda kaç cihazın işleneceği |
| `MaxMessagesPerDevicePerTick` | 2 | Bir turda tek cihaz için gönderilecek azami kayıt |
| `DelayBetweenMessages` | 4 sn | Aynı cihazın ardışık gönderimleri arasındaki bekleme |

`MaxDegreeOfParallelism` cihaz sayısı arttıkça yükseltilmelidir. Turların süresi
loglanır; sürekli aşım uyarısı alınıyorsa bu değer artırılmalıdır.

**Tcp — bağlantı ayarları**

| Ayar | Varsayılan | Açıklama |
|------|-----------|----------|
| `Host` | — | Hedef sunucu adresi |
| `Port` | — | Hedef sunucu portu |
| `ConnectTimeout` | 5 sn | Bağlantı kurma zaman aşımı |
| `SendTimeout` | 5 sn | Veri gönderme zaman aşımı |
| `NoDelay` | true | Küçük paketlerin biriktirilmeden gönderilmesi |

Sunucu farklı bir lokasyonda veya VPN üzerindeyse zaman aşımı süreleri artırılmalıdır.

**Reconnect — yeniden bağlanma**

| Ayar | Varsayılan | Açıklama |
|------|-----------|----------|
| `InitialDelay` | 1 sn | İlk başarısızlıktan sonraki bekleme |
| `MaxDelay` | 30 sn | Bekleme süresinin üst sınırı |
| `BackoffMultiplier` | 2.0 | Her denemede beklemenin kaç katına çıkacağı |
| `JitterRatio` | 0.3 | Beklemeye eklenen rastgele sapma oranı |

Bekleme süresi 1, 2, 4, 8, 16 saniye şeklinde artar ve 30 saniyede sabitlenir.
Rastgele sapma, sunucu yeniden başlatıldığında tüm istemcilerin aynı anda bağlanmaya
çalışmasını engeller.

**Outbox — kuyruk**

| Ayar | Varsayılan | Açıklama |
|------|-----------|----------|
| `MaxItemsPerDevice` | 1000 | Cihaz başına tutulacak azami kayıt |

Sınır dolduğunda en eski kayıt düşürülür. Yaklaşık 2,5 saatlik kesintiyi karşılar.

**Persistence — kalıcılık**

| Ayar | Varsayılan | Açıklama |
|------|-----------|----------|
| `SnapshotInterval` | 30 sn | Kuyruğun kaç saniyede bir kaydedileceği |
| `DatabaseEnabled` | false | Veritabanı kullanımı açık mı |
| `ConnectionString` | — | SQL Server bağlantı dizesi |
| `TableName` | dbo.OutboxTelemetry | Kuyruk tablosunun adı |
| `FileFallbackDirectory` | outbox | Veritabanına yazılamadığında kullanılacak dizin |
| `SnapshotOnShutdown` | true | Kapanışta son bir kayıt alınsın mı |

**SendPolicy — gönderim kuralları**

| Ayar | Varsayılan | Açıklama |
|------|-----------|----------|
| `ContactOnInterval` | 10 sn | Kontak açıkken gönderim aralığı |
| `ContactOffInterval` | 10 dk | Kontak kapalıyken gönderim aralığı |
| `SendOnContactChange` | true | Kontak değiştiğinde beklemeden gönder |
| `Tolerance` | 1 sn | Aralık kontrolündeki sapma payı |

`Tolerance`, zamanlayıcının milisaniye düzeyindeki sapmaları nedeniyle gönderimlerin
bir tur ötelenmesini engeller. Değiştirilmesi önerilmez.

**Idle — atıl bağlantı temizliği**

| Ayar | Varsayılan | Açıklama |
|------|-----------|----------|
| `SweepInterval` | 1 saat | Taramanın ne sıklıkla yapılacağı |
| `IdleThreshold` | 1 gün | Bir bağlantının atıl sayılması için geçmesi gereken süre |

`IdleThreshold`, kontak kapalı araçların gönderim aralığından (10 dakika) belirgin
şekilde büyük olmalıdır. Aksi halde bağlantılar sürekli açılıp kapanır.

Kuyruğunda bekleyen verisi olan bağlantılar silinmez.

**TimeZone — saat dilimi**

| Ayar | Varsayılan | Açıklama |
|------|-----------|----------|
| `TargetUtcOffsetHours` | 3 | Gönderilen verideki saat dilimi farkı |

Veriler sistem içinde UTC olarak tutulur, yalnızca gönderim anında bu değere göre
çevrilir.

---

## 7. Veritabanı kurulumu

Kuyruk kayıtları için bir tablo gereklidir. `db/OutboxTelemetry.sql` betiğini
çalıştırın:

```sql
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
```

**Gerekli izinler:** `SELECT`, `INSERT`, `DELETE`

**Tablo boyutu:** Normal işleyişte tablo neredeyse boştur; yalnızca gönderilemeyen
kayıtlar tutulur. Uzun bir kesinti sırasında cihaz sayısı × kuyruk sınırı kadar kayıt
birikebilir.

Veritabanına erişilemediğinde uygulama durmaz; kayıtlar diske yazılır ve erişim geri
geldiğinde veritabanına dönülür.

---

## 8. Yayınlama gereksinimleri

### 8.1 Kalıcı disk alanı

Veritabanına yazılamadığında kullanılan dizin **konteyner dışında kalıcı bir birim
olarak bağlanmalıdır.** Aksi halde konteyner yeniden oluşturulduğunda yedek kayıtlar
kaybolur.

| Özellik | Değer |
|---------|-------|
| Önerilen yol | `/var/lib/fleet-telemetry/outbox` |
| Ayar anahtarı | `Persistence:FileFallbackDirectory` |
| İzin | Okuma ve yazma |
| Önerilen alan | 2 GB |

Docker Compose örneği:

```yaml
services:
  app:
    volumes:
      - fleet-telemetry-outbox:/var/lib/fleet-telemetry/outbox
    environment:
      - FleetTelemetry__Persistence__FileFallbackDirectory=/var/lib/fleet-telemetry/outbox
    stop_grace_period: 30s

volumes:
  fleet-telemetry-outbox:
```

Kubernetes kullanılıyorsa `emptyDir` **yeterli değildir**; kalıcı bir birim gerekir.

### 8.2 Ağ erişimi

Uygulamanın çalıştığı ortamdan aşağıdaki hedeflere giden bağlantı açık olmalıdır:

- Hedef TCP sunucusu (`Tcp:Host` / `Tcp:Port`)
- Veri kaynağı (HTTPS)
- SQL Server

### 8.3 Kapanış süresi

Kapanışta son kayıt işleminin tamamlanabilmesi için **en az 15 saniye** düzgün kapanma
süresi tanınmalıdır.

Kubernetes: `terminationGracePeriodSeconds: 30`

### 8.4 Devreye alma öncesi kontrol listesi

- [ ] Kalıcı birim tanımlandı ve yazma izni doğrulandı
- [ ] En az 2 GB disk alanı ayrıldı
- [ ] Veritabanı tablosu oluşturuldu ve izinler verildi
- [ ] TCP sunucusu, veri kaynağı ve veritabanına ağ erişimi açık
- [ ] Kapanış süresi en az 15 saniye
- [ ] `Tcp:Host`, `Tcp:Port` ve `ConnectionString` dolduruldu
- [ ] Log çıktısı mevcut log altyapısına akıyor

---

## 9. Çalışma mantığı

### 9.1 Gönderim kuralları

| Durum | Davranış |
|-------|----------|
| Cihazdan ilk kez veri geldi | Hemen gönderilir |
| Kontak durumu değişti | Beklemeden gönderilir |
| Kontak açık | 10 saniyede bir gönderilir |
| Kontak kapalı | 10 dakikada bir gönderilir |

Kontak bilgisi kaynak verideki `isOnline` alanından okunur.

### 9.2 Bağlantı yönetimi

Bağlantılar uygulama açılışında değil, ilk veri gönderileceği anda kurulur. Kurulan
bağlantı uygulama ömrü boyunca açık tutulur ve her gönderimde yeniden kullanılır.

Bağlantı koptuğunda soket kapatılır ve yeni bir bağlantı nesnesiyle yeniden denenir.
Denemeler kademeli olarak seyrekleşir. Bir cihazın bağlantı sorunu diğerlerini etkilemez.

### 9.3 Veri kaybı koruması

1. Gönderilemeyen veriler cihaz bazlı kuyruklarda bekletilir
2. Kuyruk 30 saniyede bir veritabanına yazılır
3. Veritabanına yazılamazsa diske yazılır ve uyarı loglanır
4. Veritabanı erişimi geri geldiğinde veritabanına dönülür, diskteki artıklar temizlenir
5. Uygulama yeniden başladığında her iki kaynaktaki veriler birleştirilir, tekrar eden
   kayıtlar elenir, tarihe göre sıralanır ve kuyruğa yüklenir
6. Başarıyla gönderilen kayıtlar her iki kaynaktan da silinir

Disk yazımında cihaz başına ayrı dosya kullanılır. Yazma işlemi önce geçici bir dosyaya
yapılıp ardından taşınır; böylece yazma sırasında uygulama kapansa bile mevcut dosya
bozulmaz.

### 9.4 Dinamik cihaz yönetimi

Veri akışında görülen yeni bir cihaz için bağlantı otomatik oluşturulur. Belirlenen süre
boyunca veri göndermeyen cihazların bağlantısı kapatılır ve bellekten silinir; aynı
cihazdan yeniden veri gelirse bağlantı otomatik olarak yeniden kurulur.

---

## 10. Loglar ve izleme

### 10.1 Tur özeti

Her turun sonunda basılır:

```
Tur: toplam=20 gönderildi=5 politika=1 backoff=14 kuyrukta=462 hata=0 süre=33ms
```

| Alan | Anlamı |
|------|--------|
| `toplam` | Kaynaktan okunan cihaz sayısı |
| `gönderildi` | Başarıyla gönderilen kayıt sayısı |
| `politika` | Gönderim kuralı gereği atlanan cihaz sayısı |
| `backoff` | Bağlantı beklemesi nedeniyle atlanan cihaz sayısı |
| `kuyrukta` | Gönderilmeyi bekleyen toplam kayıt |
| `hata` | Başarısız gönderim sayısı |
| `süre` | Turun tamamlanma süresi |

### 10.2 Kayıt özeti

```
Snapshot: cihaz=20 kayit=661 hedef=Veritabani süre=515ms
```

`hedef` alanı `Veritabani` veya `Disk` değerini alır. `Disk` görülüyorsa veritabanına
erişilemiyor demektir.

### 10.3 Dikkat edilmesi gereken log satırları

| Log | Anlamı | Yapılması gereken |
|-----|--------|-------------------|
| `Tur süresi asildi` | Tur, belirlenen aralıktan uzun sürdü | Üst üste tekrarlıyorsa `MaxDegreeOfParallelism` artırılmalı |
| `veritabanina yazilamadi, diske düsülüyor` | Veritabanı erişilemiyor | Veritabanı bağlantısı kontrol edilmeli |
| `bozuk dosya karantinaya alindi` | Bir kuyruk dosyası okunamadı | O cihazın kuyruğu kaybolmuş olabilir |
| `Kuyruk geri yüklendi` | Açılışta kayıtlı veriler yüklendi | Normal, bilgi amaçlı |

Sürekli kopuk kalan cihazlar için log gürültüsü kontrol altındadır; ilk kopuş ayrıntılı,
sonraki denemeler seyrek loglanır.

---

## 11. Sorun giderme

**Hiçbir veri gönderilmiyor, loglarda sürekli bağlantı hatası var**

`Tcp:Host` ve `Tcp:Port` değerlerini kontrol edin. Uygulamanın çalıştığı ortamdan hedef
sunucuya erişim olduğundan emin olun.

**Loglarda sürekli `hedef=Disk` görünüyor**

Veritabanına erişilemiyor. Bağlantı dizesini, veritabanı sunucusunun durumunu ve ağ
erişimini kontrol edin. Uygulama çalışmaya devam eder ancak diskte veri birikir.

**`Tur süresi asildi` uyarısı sürekli geliyor**

Cihaz sayısı mevcut paralellik ayarı için fazla. `MaxDegreeOfParallelism` değerini
artırın.

**Kuyruk sürekli büyüyor, azalmıyor**

Gönderim hızı üretim hızının altında kalıyor. `DelayBetweenMessages` süresini kısaltmayı
veya `MaxMessagesPerDevicePerTick` değerini artırmayı değerlendirin.

**Uygulama açılışta hata veriyor**

Yapılandırma doğrulaması açılışta yapılır. Eksik veya geçersiz bir ayar varsa uygulama
başlamaz. Hata mesajı hangi ayarın sorunlu olduğunu belirtir.

**Yeniden başlatma sonrası veriler kaybolmuş görünüyor**

Kalıcı birimin doğru bağlandığını kontrol edin. Konteyner yeniden oluşturulduğunda
birim kalıcı değilse diskteki kayıtlar silinir.

---

## 12. Geliştirme ortamı

### 12.1 Yerel ayar dosyası

`appsettings.Development.json` dosyası **depoda yer almaz**. Yerel geliştirme için bu
dosyayı kendiniz oluşturup yalnızca ortamınıza özel değerleri yazın:

```json
{
  "FleetTelemetry": {
    "Tcp": {
      "Host": "127.0.0.1"
    },
    "Persistence": {
      "DatabaseEnabled": true,
      "ConnectionString": "Server=localhost;Database=FleetTelemetry;..."
    }
  }
}
```

Belirtilmeyen ayarlar `appsettings.json` dosyasından alınır.

### 12.2 Testler

```
dotnet test
```

Birim testleri ağ ve dosya sistemi kullanmaz, saniyeler içinde tamamlanır. Entegrasyon
testleri yerel bir dinleyici başlatır.

### 12.3 Yerel test dinleyicisi

Hedef sunucu olmadan denemek için basit bir dinleyici çalıştırılabilir:

```powershell
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 9100)
$listener.Start()
while ($true) {
    $client = $listener.AcceptTcpClient()
    Write-Host "Baglanti geldi"
}
```

---

## 13. Bilinen sınırlar

**Onay mekanizması bulunmamaktadır.** Hedef sunucunun protokol belirtimi sağlanmadığı
için gönderim, veri sokete yazıldığında başarılı sayılmaktadır. Bu, sunucunun veriyi
aldığını garanti etmez. Protokol belirtimi sağlandığında onay okuma döngüsü eklenmelidir.

**Mesaj kodlayıcı geçicidir.** Hedef protokol bilinmediği için mesajlar uzunluk öneki
ve JSON gövde biçiminde gönderilmektedir. Gerçek protokol belirtimi geldiğinde yalnızca
kodlayıcı sınıfı değiştirilecektir.

**Veri kaynağı gerçeklemesi eksiktir.** Geliştirme sırasında sahte bir veri kaynağı
kullanılmıştır. Gerçek uç nokta hazır olduğunda ilgili sınıf yazılmalıdır. Veri modeli
dönüşümü ve doğrulama katmanı hazırdır.

**Kayıt işlemi tam yazım yapar.** Her kayıt turunda ilgili cihazın tüm kayıtları silinip
yeniden yazılır. Büyük kuyruklarda veritabanı yükü oluşturabilir.

**Yük testi yapılmamıştır.** Testler sınırlı sayıda simüle cihazla gerçekleştirilmiştir.
Hedeflenen cihaz sayısıyla paralellik ayarının yeterliliği ölçülmelidir.
