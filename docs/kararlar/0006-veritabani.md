# ADR-0006: Veritabanı: uygulama kendi kullanıcısıyla, port yalnız yerel

- Durum: Yetki kısmının yerini aldı: ADR-0009 (şema ayrı `migrate` adımında, `briefyn_app` yalnız okur/yazar) · Tarih: 2026-10-17

## Bağlam
Uygulama `sa` ile bağlanıyordu, 1433 bütün ağ arayüzlerinde açıktı.

## Karar
`db-init` servisi `briefyn_app` girişini her açılışta oluşturur/eşitler (yalnız BriefynDB'de datareader/datawriter/ddladmin). Web bununla bağlanır. Port `127.0.0.1:1433`.

## Sonuçlar
Geri yükleme sonrası kullanıcı db-init ile yeniden bağlanır. Sunucu yetkisi gereken işler sa ile elle yapılır.

## Kaynak
İç proje notları
