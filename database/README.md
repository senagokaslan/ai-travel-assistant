# Yerel PostgreSQL kurulumu (Docker'sız)

Uygulama Docker gerektirmez. PostgreSQL Windows servisi olarak çalışabilir.

1. PostgreSQL 16 veya üzerini kurun ve servisinin çalıştığını doğrulayın:

   ```powershell
   Get-Service postgresql*
   ```

2. `backend/TravelAssistant.Api/appsettings.Local.example.json` dosyasını `appsettings.Local.json` adıyla kopyalayın. Bağlantı dizesindeki parolayı yerel `postgres` yönetici parolasıyla değiştirin.

3. Backend'i başlatın. Geliştirme ortamında uygulama eksik `travel_assistant` rolünü ve veritabanını oluşturur, ardından `init` klasöründeki migration'ları dosya adı sırasıyla uygular.

4. `http://localhost:5080/api/health` adresinde `database.status = healthy` değerini kontrol edin.

Uygulanan migration dosyaları `schema_migrations` tablosunda tutulur. Yeni bir şema değişikliği için `002-aciklayici-ad.sql` gibi yeni bir dosya ekleyin; daha önce uygulanmış dosyaları değiştirmeyin. Otomatik rol ve veritabanı oluşturma yalnızca geliştirme ortamında açıktır. Ayrı yönetici kimliği gereken ortamlarda `ConnectionStrings__PostgresAdmin` kullanılabilir.
