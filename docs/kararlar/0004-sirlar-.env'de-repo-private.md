# ADR-0004: Sırlar .env'de, repo private

- Durum: Kabul edildi · Tarih: 2026-10-16

## Bağlam
appsettings.json ve docker-compose.yml'de şifreler açıktı, repo public'ti.

## Karar
Repo private. Bütün sırlar git'e girmeyen `.env`'de; compose `${...}` ile okur. Sır rotasyonu (SA, Gmail uygulama şifresi) Ulaş'ın 2026-10-17 kararıyla planlardan çıkarıldı.

## Sonuçlar
Docker'sız `dotnet run` değerleri ortam değişkeni/user-secrets ile ister. Eski sırlar git geçmişinde durur.

## Kaynak
İç proje notları
