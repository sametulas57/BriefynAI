# ADR-0008: Aksiyon atama: düz cümle sürümü

- Durum: Kabul edildi · Tarih: 2026-10-18

## Bağlam
Aksiyon takibi için iki ayrı tasarım denendi: önce beş parçalı şablon, sonra düz cümle.

## Karar
Ulaş düz cümle sürümünü seçti: ActionItem'e sorumlu, son tarih, tamamlayan ve tamamlanma zamanı; `/MyActions` "Bana Atananlar". Şablon sürümü geri alındı (revert 851707e).

## Sonuçlar
E-posta hatırlatma, gecikme grupları yok (bilerek). Takvime ekleme (.ics) Ulaş söyleyince ayrı iş.

## Kaynak
İç proje notları
