# API Kılavuzu

Backend geliştirme ortamında `http://localhost:5080` adresinde çalışır. Güncel OpenAPI arayüzü `http://localhost:5080/swagger` adresindedir.

Bu belge ana işlemleri ve PowerShell örneklerini açıklar. JSON alanlarının kesin ve güncel sözleşmesi için Swagger kaynağı esas alınır.

## Kimlik doğrulama

Kayıt ve giriş dışındaki kullanıcıya özel işlemler `Authorization: Bearer <token>` başlığı kullanır. Token giriş cevabında üretilir, frontend tarafından `sessionStorage` içinde tutulur ve backend yeniden başladığında geçersiz olur.

Yerel örnek hesap oluşturma:

```powershell
$api = "http://localhost:5080"
$registerBody = @{
  name = "Demo Kullanıcı"
  email = "demo.user@example.test"
  password = "DemoOnly123!"
} | ConvertTo-Json

Invoke-RestMethod "$api/api/auth/register" `
  -Method Post `
  -ContentType "application/json" `
  -Body $registerBody
```

Giriş ve token'ı yalnızca mevcut PowerShell oturumunda tutma:

```powershell
$loginBody = @{
  email = "demo.user@example.test"
  password = "DemoOnly123!"
} | ConvertTo-Json

$login = Invoke-RestMethod "$api/api/auth/login" `
  -Method Post `
  -ContentType "application/json" `
  -Body $loginBody

$headers = @{ Authorization = "Bearer $($login.token)" }
```

`DemoOnly123!` yalnızca boş, yerel eğitim veritabanında komutun çalışmasını sağlayan herkese açık örnektir; başka hiçbir ortamda kullanmayın. Gerçek parolayı veya `$login.token` değerini ekrana yazdırmayın, kaydetmeyin ve commit etmeyin.

## Endpoint özeti

| Yöntem | Adres | Yetki | Amaç |
| --- | --- | --- | --- |
| `GET` | `/api/health` | Yok | API ve PostgreSQL sağlık durumu |
| `POST` | `/api/auth/register` | Yok | Yerel kullanıcı hesabı oluşturma |
| `POST` | `/api/auth/login` | Yok | Bellek içi oturum tokenı oluşturma |
| `GET` | `/api/auth/me` | Kullanıcı | Aktif kullanıcıyı doğrulama |
| `POST` | `/api/auth/logout` | Kullanıcı | Oturumu sonlandırma |
| `GET` | `/api/hotels/locations` | Yok | Şehir/otel önerileri |
| `GET` | `/api/hotels` | Yok | Tarih, oda ve misafire göre otel arama |
| `GET` | `/api/hotels/{id}` | Yok | Otel ve uygun oda ayrıntısı |
| `GET` | `/api/travel/airports` | Yok | Şehir, havaalanı adı veya IATA ile arama |
| `GET` | `/api/flights` | Yok | Doğrudan/aktarmalı uçuş seçenekleri |
| `POST` | `/api/bookings/summary/hotel` | Yok | Otel seçimini güncel fiyat/stokla özetleme |
| `POST` | `/api/bookings/summary/flight` | Yok | Uçuş seçimini güncel fiyat/stokla özetleme |
| `POST` | `/api/bookings/details/*/validate` | Yok | Misafir/yolcu ve iletişim bilgilerini doğrulama |
| `POST` | `/api/bookings/confirm/*` | Kullanıcı | Atomik ve idempotent rezervasyon simülasyonu oluşturma |
| `GET` | `/api/bookings` | Kullanıcı | Yalnızca aktif kullanıcının kayıtlarını listeleme |
| `GET` | `/api/bookings/{id}` | Kullanıcı | Kullanıcıya ait kayıt ayrıntısı |
| `POST` | `/api/bookings/{id}/cancel` | Kullanıcı | Açık onayla uygun kaydı iptal etme |
| `GET/POST` | `/api/chat/conversations` | Kullanıcı | Konuşmaları listeleme/yeni konuşma |
| `GET/POST` | `/api/chat/conversations/{id}/messages` | Kullanıcı | Mesaj geçmişi/gönderim |
| `GET/PATCH` | `/api/profile` | Kullanıcı | Profil görüntüleme/güncelleme |
| `GET/POST/PATCH` | `/api/admin/*` | Admin | Örnek katalog yönetimi |

## Otel arama ve özet örneği

Örnek veri takvimi temiz kurulum gününden itibaren oluşturulur. Aşağıdaki komut göreli tarih kullandığı için yeni kurulumda doğrudan çalışır:

```powershell
$api = "http://localhost:5080"
$checkIn = (Get-Date).Date.AddDays(14).ToString('yyyy-MM-dd')
$checkOut = (Get-Date).Date.AddDays(16).ToString('yyyy-MM-dd')

$hotels = Invoke-RestMethod `
  "$api/api/hotels?q=Antalya&checkIn=$checkIn&checkOut=$checkOut&adults=2&rooms=1&children=0"

$selectedHotel = $hotels[0]
$selectedOption = $selectedHotel.options[0]

$summaryBody = @{
  hotelId = $selectedHotel.id
  optionKey = $selectedOption.key
  checkIn = $checkIn
  checkOut = $checkOut
  rooms = 1
  adults = 2
  children = 0
  childAges = @()
  quotedTotal = $selectedOption.totalPrice
  quotedCurrency = "TRY"
} | ConvertTo-Json

$summary = Invoke-RestMethod "$api/api/bookings/summary/hotel" `
  -Method Post `
  -ContentType "application/json" `
  -Body $summaryBody

$summary | ConvertTo-Json -Depth 8
```

Özet endpoint'i istemcinin gönderdiği fiyatı güvenilir kabul etmez; mevcut oda fiyatı ve müsaitliği veritabanından tekrar okur. Fiyat değiştiyse cevap yeni tutarı belirtir.

## Uçuş arama örneği

Temiz kurulumda zengin uçuş kataloğu migration gününden 14 gün sonrası için `IST → AYT`, 18 gün sonrası için `AYT → IST` seçenekleri ekler:

```powershell
$api = "http://localhost:5080"
$departure = (Get-Date).Date.AddDays(14).ToString('yyyy-MM-dd')
$return = (Get-Date).Date.AddDays(18).ToString('yyyy-MM-dd')

Invoke-RestMethod `
  "$api/api/flights?from=IST&to=AYT&date=$departure&returnDate=$return&tripType=round-trip&adults=2&children=0&infants=0"
```

Eski bir veritabanında migration yalnızca bir kez çalıştığı için bu göreli tarihler mevcut seed tarihleriyle eşleşmeyebilir. Böyle bir durumda UI üzerinden katalogdaki mevcut uçuş tarihini kullanın veya yerel veritabanındaki `flight_legs.departure_at` değerlerini yalnızca okuyarak kontrol edin.

## Rezervasyon onayı

Onay isteği şunları içerir:

- Tekrar denemelerde aynı kalan UUID biçimli `requestKey`
- Daha önce özetlenen `selection`
- Aramadaki kişi sayısıyla birebir eşleşen `travelers`
- Bir yetişkine bağlı `contact`

Örnek otel onay gövdesi:

```json
{
  "requestKey": "11111111-2222-4333-8444-555555555555",
  "details": {
    "selection": {
      "hotelId": "<OTEL_UUID>",
      "optionKey": "<ODA_SECENEGI>",
      "checkIn": "2026-10-20",
      "checkOut": "2026-10-22",
      "rooms": 1,
      "adults": 2,
      "children": 0,
      "childAges": [],
      "quotedTotal": 7800,
      "quotedCurrency": "TRY"
    },
    "travelers": [
      { "type": "adult", "firstName": "Demo", "lastName": "Kullanıcı", "age": null, "accompanyingAdultIndex": null },
      { "type": "adult", "firstName": "Örnek", "lastName": "Yolcu", "age": null, "accompanyingAdultIndex": null }
    ],
    "contact": {
      "adultIndex": 0,
      "email": "demo.user@example.test",
      "phone": "+905551112233"
    }
  }
}
```

Bu gövdedeki UUID, seçenek ve fiyatlar açıklama amaçlıdır. Gerçek istekte hemen önce alınan özet değerlerini kullanın. Aynı `requestKey` ve aynı kullanıcıyla tekrarlanan başarılı onay ikinci kayıt oluşturmaz.

## Listeleme ve iptal

```powershell
$bookings = Invoke-RestMethod "$api/api/bookings" -Headers $headers
$booking = $bookings[0]

Invoke-RestMethod "$api/api/bookings/$($booking.id)" -Headers $headers

$cancelBody = @{ confirmed = $true } | ConvertTo-Json
Invoke-RestMethod "$api/api/bookings/$($booking.id)/cancel" `
  -Method Post `
  -Headers $headers `
  -ContentType "application/json" `
  -Body $cancelBody
```

İptal yalnızca kullanıcıya ait, başlamamış ve `simulated` durumundaki kayıt için uygulanır. İkinci iptal stok üzerinde ikinci değişiklik yapmaz.

## Hata biçimi

Beklenen API hataları uygun HTTP durum koduyla ve kullanıcıya gösterilebilir sade mesajla döner. Ciddi/beklenmeyen hata teknik ayrıntıyı istemciye açmaz; cevapta takip için bir correlation kimliği bulunabilir. Parola, bağlantı dizesi, token ve kişisel bilgiler loglarda maskelenir.
