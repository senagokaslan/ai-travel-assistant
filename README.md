# Bağımsız AI Destekli Seyahat Asistanı

Bağımsız AI Destekli Seyahat Asistanı, yerel örnek veriler ve yapay zekâ destekli sohbetle otel ve uçuş aramayı, seçenekleri karşılaştırmayı amaçlayan rezervasyon simülasyonu oluşturmayı sağlar.

> Bağımsız AI Destekli Seyahat Asistanı gerçek rezervasyon oluşturmaz, uçak bileti düzenlemez ve gerçek ödeme almaz; hiçbir işlemi otel, havayolu veya üçüncü taraf sağlayıcılara göndermez.

## Projenin sundukları

- Form veya sohbet üzerinden örnek otel ve uçuş verilerinde arama
- Sonuçları fiyat, özellik, yıldız, havayolu ve aktarma gibi ölçütlerle karşılaştırma
- Kullanıcı onayıyla rezervasyon simülasyonu kaydı oluşturma
- Simülasyon kayıtlarını görüntüleme ve uygun olanları iptal etme
- Admin rolüyle örnek seyahat, fiyat ve müsaitlik verilerini yönetme

## Kapsam dışı işlemler

- Gerçek kredi kartı veya ödeme işlemi
- Gerçek otel rezervasyonu oluşturma ya da bir otele gönderme
- Gerçek uçak bileti düzenleme ya da bir havayoluna gönderme
- Üçüncü taraf seyahat sağlayıcısında işlem başlatma
- Gerçek kimlik veya ödeme bilgisi toplama

Proje kimliği ve ekranlarda kullanılacak değişmez metinler [proje kimliği belgesinde](docs/proje-kimligi-ve-kapsami.md) tanımlanmıştır. Makine tarafından okunabilir tek doğruluk kaynağı [project-identity.json](content/project-identity.json) dosyasıdır.

## Teknoloji ve klasörler

- `backend/TravelAssistant.Api`: .NET 8 minimal Web API
- `frontend`: React, TypeScript ve Vite arayüzü
- `database/init`: PostgreSQL başlangıç şeması
- `docker-compose.yml`: isteğe bağlı PostgreSQL yardımcı yapılandırması (uygulama Docker'a bağlı değildir)

## Yerel kurulum

Gereksinimler: .NET 8 SDK, Node.js 20 veya üzeri, npm ve yerel PostgreSQL 16 veya üzeri.

1. Backend için yerel ayar dosyasını kopyalayın:

   ```powershell
   Copy-Item backend/TravelAssistant.Api/appsettings.Local.example.json backend/TravelAssistant.Api/appsettings.Local.json
   ```

   Bağlantı dizesindeki parola değerini PostgreSQL kurulumu sırasında belirlediğiniz yerel `postgres` yönetici parolasıyla değiştirin. Bu parola yalnızca Git tarafından yok sayılan yerel dosyada kalır.

2. Yerel PostgreSQL servisini başlatın. Geliştirme ortamında backend ilk açılışta eksik `travel_assistant` rolünü ve veritabanını otomatik oluşturur; sürümlü SQL migration'larını uygular. pgAdmin'de elle veritabanı, tablo veya örnek veri oluşturmanız gerekmez.

   Docker kullanmak isterseniz aynı veritabanı ayarlarını `docker-compose.yml` ile de başlatabilirsiniz; bu zorunlu değildir. Paylaşılan veya canlı ortamlarda otomatik veritabanı oluşturma kapalıdır; yalnızca mevcut veritabanına bekleyen migration'lar uygulanır.

3. Bir terminalde backend'i başlatın:

   ```powershell
   dotnet run --project backend/TravelAssistant.Api
   ```

   API `http://localhost:5080`, Swagger `http://localhost:5080/swagger` ve sağlık kontrolü `http://localhost:5080/api/health` adresinde açılır.

4. İkinci terminalde arayüzü başlatın:

   ```powershell
   npm --prefix frontend install
   npm --prefix frontend run dev
   ```

   Başlangıç ekranını `http://localhost:5173` adresinde açın. Arayüz `/api` isteklerini yerel backend'e yönlendirir.

### İsteğe bağlı AI anlayıcı

Sohbet, AI yapılandırılmadığında güvenli ve sınırlı yerel anlayıcıyla çalışmaya devam eder. Uyumlu bir AI çıkarım ağ geçidi kullanmak için gizli değerleri yalnızca yerel ortamda tanımlayın:

```powershell
$env:AiAssistant__Endpoint = "https://ai-gateway.example/parse-travel"
$env:AiAssistant__ApiKey = "yerel-gizli-anahtar"
$env:AiAssistant__TimeoutSeconds = "4"
```

AI ağ geçidinin yanıtı yalnızca tanımlı seyahat alanlarını içermelidir. Bilinmeyen alanlar, geçersiz değerler, bozuk JSON, zaman aşımı ve servis hataları reddedilir; kullanıcı sınırlı anlayıcıya ve klasik arama formuna yönlendirilir. AI çıktısından fiyat, stok, ürün veya rezervasyon bilgisi kabul edilmez. Bu bilgiler her zaman PostgreSQL kayıtlarından hesaplanır. Gerçek anahtarları ayar dosyalarına, loglara veya Git'e eklemeyin.

## Otomatik testler

PostgreSQL çalışırken bütün backend testlerini proje kökünden çalıştırın:

```powershell
dotnet test TravelAssistant.slnx --configuration Release
```

Testler bağlantıyı önce `ConnectionStrings__Postgres` ortam değişkeninden, yoksa Git tarafından izlenmeyen `backend/TravelAssistant.Api/appsettings.Local.json` dosyasından okur. Her test çalıştırması rastgele adlandırılmış, geçici bir PostgreSQL şeması kullanır ve bitince bu şemayı siler; mevcut uygulama tablolarına ve örnek verilere yazmaz. Paket; otel ve uçuş aramasını, geçersiz tarih ve konumu, kapasiteyi, konuşma bağlamını, rezervasyon kaydını, kullanıcı sahipliğini ve aynı son koltuk için eş zamanlı işlemi kapsar.

## Sayfalar ve yönlendirme

| Adres | Erişim | İçerik |
| --- | --- | --- |
| `/` | Herkes | Ana sayfa ve sistem durumu |
| `/hotels` | Herkes | Otel arama başlangıç ekranı |
| `/hotels/results` | Herkes | Doğrulanmış ölçütleri URL'de koruyan otel sonuç ekranı |
| `/flights` | Herkes | Uçuş arama başlangıç ekranı |
| `/booking/summary` | Herkes | Seçilen otel veya uçuşun güncel fiyat, müsaitlik ve koşul özeti |
| `/chat` | Herkes | Sohbetle arama başlangıç ekranı |
| `/bookings` | Giriş gerekli | Kullanıcının simülasyon kayıtları |
| `/profile` | Giriş gerekli | Profil ve tercihler |
| `/admin` | Admin gerekli | Örnek seyahat verisi yönetimi |
| `/login` | Herkes | Demo kullanıcı/admin girişi |

Korumalı bir adrese anonim gidildiğinde kullanıcı `/login?returnTo=...` adresine yönlendirilir. Demo girişinden sonra hedef sayfaya geri dönülür. Admin bağlantısı normal kullanıcı menüsünde gösterilmez; yetkisiz doğrudan `/admin` erişimi açıklayıcı bir yetki ekranına düşer. Tanınmayan adresler anlaşılır bir 404 ekranı gösterir.

## Bağlantı sorunları

- Sağlık kontrolü “bağlantı ayarı bulunamadı” diyorsa `appsettings.Local.json` dosyasını oluşturun veya `ConnectionStrings__Postgres` ortam değişkenini tanımlayın.
- Otomatik kurulum başarısızsa PostgreSQL servisinin çalıştığını ve yerel ayardaki parolanın `postgres` yönetici parolasıyla aynı olduğunu kontrol edin. Ayrı yönetici bilgisi gereken ortamlarda `ConnectionStrings__PostgresAdmin` tanımlanabilir.
- Yeni migration eklemek için `database/init` altına sıralı yeni bir `.sql` dosyası ekleyin. Uygulanan dosyalar `schema_migrations` tablosunda izlenir ve yeniden çalıştırılmaz.
- PostgreSQL servisi kurulu değilse Windows PostgreSQL kurulumunu tamamlayın ve servis durumunu `Get-Service postgresql*` ile kontrol edin. Docker veya WSL gerekmez.
- `.env` ve `appsettings.Local.json` Git tarafından yok sayılır. Gerçek parolaları, API anahtarlarını veya bağlantı bilgilerini örnek dosyalara eklemeyin.
