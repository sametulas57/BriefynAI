# ADR-0003: Şifreleme: AES-256-GCM (v2)

- Durum: Biçim kısmının yerini aldı: ADR-0010 · Tarih: 2026-10-17

## Bağlam
Eski AES-CBC anahtarı 16 Ekim'e kadar public repo geçmişindeydi; CBC bütünlük sağlamıyor.

## Karar
Hassas alanlar `v2:` önekli AES-256-GCM ile, kolonlar nvarchar(max). `reencrypt --dry-run/--apply` ile taşındı. Eski anahtar ve CBC yolu kaldırıldı (S4-D). Yorumlar da v2 ile şifreli.

## Sonuçlar
Şifreli kolonda SQL ile arama/eşitlik yapılamaz. Eski yedekleri açmak için eski anahtar `.env`'de "SİLME" notuyla duruyor.

## Kaynak
İç proje notları
