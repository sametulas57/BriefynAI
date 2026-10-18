# ADR-0007: Data Protection anahtarları kalıcı

- Durum: Kabul edildi · Tarih: 2026-10-17

## Bağlam
Anahtarlar container içindeydi; her yeniden kurulumda oturumlar düşüyor, formlar antiforgery hatası veriyordu.

## Karar
Anahtarlar `App_Data/keys`'te (Docker bağlamasıyla host'ta kalıcı), `SetApplicationName("Briefyn")`.

## Sonuçlar
Anahtarlar diskte şifresiz; yayında sertifikayla korunmalı.

## Kaynak
İç proje notları
