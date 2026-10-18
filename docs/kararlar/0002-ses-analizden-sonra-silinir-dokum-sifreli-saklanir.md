# ADR-0002: Ses analizden sonra silinir, döküm şifreli saklanır

- Durum: Kabul edildi · Tarih: 2026-10-17

## Bağlam
1 saatlik ses ≈ 50–60 MB, döküm ≈ 50–100 KB. Ses en hassas veri; döküm yeniden özetleme ve kontrol için yeterli.

## Karar
Ses özel depoda (App_Data, /uploads'tan servis edilmez), başarılı analizden sonra silinir; başarısızlar 7 gün sonra temizlenir. Döküm (Transkript) v2 ile şifreli saklanır; yeniden deneme yalnız özeti üretir.

## Sonuçlar
Eski kayıtların sesi geri gelmez. Yeniden özetleme STT'ye yeniden ödeme yapmadan mümkün.

## Kaynak
İç proje notları
