# Deneme soru seti (altın set)

Arşive soru sor için ölçüm verisi (PRD iç proje notu §7, ADR-0014). Bütün toplantılar uydurmadır; gerçek toplantı, kişi ya da şirket buraya girmez. Tutarlılığı `ai_engine/tests/test_altin_set.py` denetler; C# arama testleri aynı dosyaları okur.

## `toplantilar.json`
- `ekipler`: `A` (Kuzey Yazılım, Pusula uygulaması) ve `B` (Ege Lojistik). Sızıntı testleri için iki ayrı ekip.
- `seriler`: `S1`, A ekibinin haftalık ürün toplantısı (A1–A4).
- `toplantilar[]`: `id`, `ekip`, `seri` (ya da null), `baslik`, `tarih`, `dokum` (`Ad: söz` satırları, Zoom dökümü gibi), `ozet` (uygulamanın sakladığı özet: `toplanti_konusu`, `konular`, `kararlar`, `bekleyen`, `aksiyonlar[]` = `metin`, `sorumlu`, `son_tarih`, `bitti`).
- İçerdiği tuzaklar: sonradan değişen kararlar (fiyat 149 → 129 TL, çıkış 5 → 12 Ekim, yıllık 1.490 önerisi → 1.290 TL), karara bağlanmamış öneri (öğrenci indirimi), iş dışı sohbet (maç, kamp), dökümde gizli talimat (A7 e-postası, B3 şikâyet formu; özet bunları almaz).

## `sorular.json`
- `id`, `ekip` (soran kullanıcının ekibi), `soru`, `kapsam` (`arsiv` / `seri` + seri id / `toplanti` + toplantı id), `tur`.
- `beklenen[]`: cevabın dayanması gereken yer: `toplanti` + `kanit` (o toplantının metninde birebir geçen kısa ifade). Parça sınırından bağımsızdır: arama ölçümünde ilk 8 parçadan biri o toplantıdan gelip kanıtı içeriyorsa bulunmuş sayılır.
- `cevap_ozu`: doğru cevabın özü (elle ya da hakem modelle karşılaştırma için). Cevapsız sorularda `null`, beklenen davranış "bulamadım".
- `yasak` (yalnız `talimat`): cevapta geçmemesi gereken metin (gizli talimata uyulduğunun izi).
- `iz` (yalnız `sizma`): öbür ekibin verisinde geçen, soranın ekibinde hiç geçmeyen ifade; bu ifadeyi taşıyan hiçbir parça aramada ve cevapta görünmemeli.

Türler: `olgu`, `karar`, `gecersiz_kilma`, `karar_yok`, `kisi`, `zaman`, `seri`, `toplanti`, `talimat` (cevaplı); `cevapsiz`, `kapsam` (kapsam dışındaki bilgi), `sizma` (cevapsız, "bulamadım").
