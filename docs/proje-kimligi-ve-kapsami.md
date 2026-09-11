# Bağımsız AI Destekli Seyahat Asistanı - Proje Kimliği ve Kapsamı

Bu belge, giriş sayfası, ana sayfa ve proje tanıtımında kullanılacak metinlerin tek ve değişmez karşılığını tanımlar.

## Ortak tanıtım metni

**Proje adı:** Bağımsız AI Destekli Seyahat Asistanı

**Kısa açıklama:** Bağımsız AI Destekli Seyahat Asistanı, yerel örnek veriler ve yapay zekâ destekli sohbetle otel ve uçuş aramayı, seçenekleri karşılaştırmayı ve eğitim amaçlı rezervasyon simülasyonu oluşturmayı sağlar.

**Hizmet sınırı:** Bağımsız AI Destekli Seyahat Asistanı gerçek rezervasyon oluşturmaz, uçak bileti düzenlemez ve gerçek ödeme almaz; hiçbir işlemi otel, havayolu veya üçüncü taraf sağlayıcılara göndermez.

Bu üç metin giriş sayfasında, ana sayfada ve proje tanıtımında anlamı değiştirilmeden kullanılmalıdır. Arayüz tamamlandığında metinler kopyalanmamalı; `content/project-identity.json` dosyasından okunmalıdır.

## Ekran kullanımı

### Giriş sayfası

- Başlık: `Bağımsız AI Destekli Seyahat Asistanı`
- Açıklama: ortak kısa açıklama
- Uyarı: ortak hizmet sınırı; formun hemen yakınında ve kullanıcı giriş yapmadan önce görünür olmalı

### Ana sayfa

- Başlık: `Bağımsız AI Destekli Seyahat Asistanı`
- Açıklama: ortak kısa açıklama
- Uyarı: ortak hizmet sınırı; ilk görünümde veya tanıtım alanının hemen altında görünür olmalı

### Proje tanıtımı

- Başlık, kısa açıklama ve hizmet sınırı yukarıdaki ortak metinlerle birebir aynı olmalı
- Özellik listesinde yalnızca örnek veriler, arama, karşılaştırma ve rezervasyon simülasyonu anlatılmalı

## Dil kuralları

Yanıltıcı ifadeler yerine aşağıdaki karşılıklar kullanılmalıdır:

| Kullanılmamalı | Kullanılmalı |
| --- | --- |
| Rezervasyon yap | Rezervasyon simülasyonu oluştur |
| Rezervasyon tamamlandı | Simülasyon kaydı oluşturuldu |
| Bilet satın al | Uçuş seçeneğini simülasyona ekle |
| Ödeme yap / Ödemeye geç | Özeti onayla |
| Gerçek fiyat / gerçek stok | Örnek fiyat / örnek müsaitlik |
| Satın alma başarılı | Simülasyon başarıyla kaydedildi |

Arama sonuçlarında gösterilen tutarlar örnek veridir. “Toplam tutar” ifadesi kullanılabilir; ancak ödeme alınacağına veya dış bir sağlayıcıda fiyatın geçerli olduğuna dair söz verilmemelidir.

## Kapsam doğrulaması

- Uygulama dış sağlayıcılardan canlı otel veya uçuş verisi almaz.
- Uygulama otel, havayolu veya seyahat sağlayıcısına rezervasyon göndermez.
- Uygulama kredi kartı, banka hesabı veya gerçek kimlik belgesi istemez.
- Oluşturulan kayıtlar yalnızca proje veritabanındaki eğitim amaçlı simülasyon kayıtlarıdır.
- Yapay zekâ yalnızca isteği anlamaya ve sonuçları sunmaya yardım eder; örnek fiyat ve müsaitlik bilgileri proje verilerinden gelir.
