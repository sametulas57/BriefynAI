# ADR-0004: Sırlar .env'de

- Durum: Kabul edildi · Tarih: 2026-10-16 · Güncelleme: 2026-11-06

## Bağlam
Şifreler ve anahtarlar kodda, ayar dosyalarında ya da git geçmişinde durmamalı. Repo açıktır.

## Karar
Bütün sırlar git'e girmeyen `.env`'de; compose `${...}` ile okur. `.env.example` yalnız ayar adlarını gösterir, değer içermez.

## Sonuçlar
Docker'sız `dotnet run` değerleri ortam değişkeni ya da user-secrets ile ister.

## Kaynak
İç proje notları
