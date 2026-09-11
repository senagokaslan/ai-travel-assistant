# Yerel PostgreSQL kurulumu (Docker'sız)

Uygulama Docker gerektirmez. PostgreSQL Windows servisi olarak çalışabilir.

1. PostgreSQL 16 veya üzerini kurun ve servisinin çalıştığını doğrulayın:

   ```powershell
   Get-Service postgresql*
   ```

2. PostgreSQL yöneticisiyle veritabanı ve uygulama kullanıcısını oluşturun:

   ```sql
   CREATE USER travel_assistant WITH PASSWORD 'yerel-gelistirme-parolaniz';
   CREATE DATABASE travel_assistant OWNER travel_assistant;
   ```

3. `backend/TravelAssistant.Api/appsettings.Local.json` içindeki bağlantı dizesinde aynı kullanıcı, parola, veritabanı ve `localhost:5432` bilgilerini kullanın.

4. `init/001-initialize.sql` dosyasını `travel_assistant` veritabanına pgAdmin Query Tool veya `psql` ile bir kez uygulayın.

5. Backend'i başlatıp `http://localhost:5080/api/health` adresinde `database.status = healthy` değerini kontrol edin.
