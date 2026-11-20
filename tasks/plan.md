# Uygulama planı: Görevleri takvime aktarma

Spec: `SPEC.md` (onaylı, 2026-11-20) · Görev listesi: `tasks/todo.md` · Durum: onaylandı (Ulaş, 2026-11-20)

## Özet

Atanmış ve son tarihli görev için görevli kişiye e-postayla takvim daveti gider. Görev değişince aynı kayıt güncellenir ya da iptal edilir; "Bana Atananlar"da yeniden gönderme düğmesi ve takvim dosyası indirme olur. İş dokuz göreve bölündü. En riskli kısım, Outlook'un daveti gerçekten tanıyıp tanımadığı; bu yüzden ilk dilim (görev 1-4) atamadan Outlook'taki kayda kadar uçtan uca çalışır ve hemen gerçek Outlook'ta denenir. Gerisi o deneme geçerse yapılır.

## Başlangıç koşulu

`deneme/azure-konusmaci` dalındaki konuşmacı işi main'e girer, iş main'den başlar (spec kararı). Proje aşaması "yayında değil": main'e PR'sız push, CI push'tan sonra koşar.

## Kodda baktıklarım (planın dayanağı)

- Görevin değiştiği yerler: `Details:PostAssignAction` (atama, son tarih, atama kaldırma), `Details:PostToggleTask` (bitti/açık), `MyActions:PostComplete` ve `PostReopen`.
- Görevin kaybolduğu yerler: `History:PostDelete` (toplantı silinince görevleri de silinir), `Workspace/Members:PostRemoveMember`, `PostLeaveWorkspace` (görev kalır ama kişi ekipte değildir), `PostDeleteWorkspace`, hesap silme (`AccountDeletionService` görevlinin kimliğini toplu sorguyla boşaltır, sayfa işleyicisinden geçmez).
- Yapay zekânın yeniden özetlemesi yalnız takip edilmeyen görevleri siler (`MeetingProcessor.IsTracked`: atanmış ya da tarihli görev kalır). Davetli görev bu yoldan kaybolmaz.
- E-posta: `EmailService` her gönderimde kendi `SmtpClient`'ını kurar. Testlerde `BriefynFactory.EmailServiceOverride` ve `FakeBackgroundJobClient.CountFor` var.
- Zamanlanmış işler `Program.cs`'te `RecurringJob` ile Türkiye saatine göre kurulu (`overdue-reminders` örneği).
- `WorkspaceBoundaryTests` her sayfa işleyicisini listede arar; yeni işleyici listeye girmezse test kırılır.

## Mimari kararlar

1. **Tek eşitleme işi.** Her değişiklik yalnız `ICalendarSyncService.SyncAsync(actionItemId)`'yi kuyruğa atar. İş, olması gerekeni (görev var mı, atanmış mı, tarihli mi, bitmemiş mi, kişi hâlâ ekipte mi, hesabı duruyor mu) gönderilmiş davet kaydıyla karşılaştırır; davet, güncelleme ya da iptal gönderir. Aynı iş iki kez koşarsa ikincisi fark bulmaz. Sayfalara takvim mantığı girmez.
2. **Davet kaydı görevden bağımsız yaşar.** `CalendarInvites` tablosu: görev kimliği (yabancı anahtar yok), kişi kimliği, UID, sıra no, gönderilen tarih, durum (etkin / iptal), `RowVersion`. (görev, kişi) için tek satır. Toplantı silinse de satır kalır, iptal gidince silinir. İptal edilmiş satır görev yeniden açılırsa aynı UID ile yeniden kullanılır.
3. **Önce kayıt, sonra gönderim.** İş önce satırı "gönderilecek" işaretiyle ve yeni sıra noyla kaydeder, sonra e-postayı gönderir, sonra işareti kaldırır. Gönderim başarısız olursa Hangfire'ın yeniden denemesi işareti görür ve tekrar gönderir. Aynı anda koşan iki iş `RowVersion` çakışmasıyla ayrılır; kaybeden iş yeniden okur, fark kalmadıysa çıkar.
4. **Tercih yalnız yeni otomatik daveti durdurur.** Profil'de anahtarı kapatan kişiye yeni otomatik davet gitmez. Takviminde zaten duran kayıtlar güncellenmeye ve iptal edilmeye devam eder; yoksa takvimde eski tarih kalır. Spec'te açık yazmıyordu; planla birlikte onaylandı (Ulaş, 2026-11-20).
5. **Düğme zorla gönderir.** "Takvimime gönder" fark olmasa da daveti sıra noyu artırarak yeniden yollar (silinen davet geri gelsin diye). Kişi başı sınır var; sayı paylaş düğmesine bakılarak görev 5'te belirlenir.
6. **Günlük süpürme güvenlik ağıdır.** Ekip silme ve ekip sahibinin hesap silmesi gibi seyrek yollar ayrı ayrı bağlanmaz. Her gün etkin davet kayıtları için eşitleme koşar, kaçan iptali yakalar. Sık yollar (atama, bitti, toplantı silme, ekipten çıkma) anında tetiklenir.
7. **Hesabı silinen kişiye e-posta gitmez.** Hesap silmede o kişinin davet kayıtları sessizce silinir.

## Bağımlılık sırası

```
1 .ics üretici ──┐
2 tablo + ayarlar ┴─ 3 eşitleme + e-posta ── 4 toplantı sayfası tetikleri ── [Kontrol A: gerçek Outlook]
                                                   │
                       5 Bana Atananlar (tetik, düğme, indirme) ── 6 Profil anahtarı ── [Kontrol B]
                                                   │
                       7 toplantı silme + ekipten çıkma ── 8 günlük süpürme + hesap silme ── 9 belgeler ── [Kontrol C]
```

Görev 1 ile 2 birbirinden bağımsız; aynı anda yapılabilir. Görev 3'ten sonrası sırayla, çünkü hepsi eşitleme servisini ve aynı test dosyasını kullanıyor.

## Kim yapar (CLAUDE.md iş dağıtımı)

- Uygulama Codex'e (`gpt-6.1-sol`), adım adım brifle. Brifte Addy skill'lerinin adı yazılır ve önce okunması istenir: `incremental-implementation`, `test-driven-development`; görev 5 ve 7'de ayrıca `security-and-hardening`.
- Görev 5 ve 6'daki görünen parçalar (düğme, anahtar, metin) Claude Opus alt ajanında; sayfanın mevcut görünüşüne uyar, yeni görünüm getirmez.
- Her görevden sonra diff'i ben okurum; kontrol noktalarında `scripts/verify.sh`.

## Riskler

| Risk | Etki | Önlem |
|---|---|---|
| Outlook daveti takvim daveti olarak tanımıyor (`System.Net.Mail` parçaları yanlış sırada ya da yanlış başlıkla) | Yüksek | Görev 3'te e-posta biçimi testle sabitlenir; Kontrol A'da gerçek Outlook web'de hemen denenir. Geçmezse durulur, sonraki görevlere geçilmez. |
| Güncelleme yeni kayıt açıyor, iptal kaydı kaldırmıyor | Yüksek | Aynı UID, artan sıra no ve aynı düzenleyen adresi testle zorunlu; Kontrol A'da üç adım da denenir. |
| Kişisel posta sunucusu (gönderici adresi) davetleri gereksiz klasörüne atıyor | Orta | Kontrol A'da gelen kutusu kontrol edilir; düşerse sağlayıcı değişikliği "önce sor" maddesi. |
| Aynı görev için iki iş aynı anda koşup iki e-posta gönderiyor | Orta | Karar 3 (`RowVersion` + "gönderilecek" işareti); eşzamanlılık testi. |
| Klasik masaüstü Outlook ve iş hesabı denenemiyor | Orta | Spec'teki gibi raporda bilinen risk; yayından sonra ilk kurumsal kullanıcıdan geri bildirim. |
| Kişi gün içinde tarihi çok kez değiştirip çok e-posta alıyor | Düşük | Kabul edildi. Her değişiklik bir güncelleme; gerekirse sonra birleştirme eklenir. |

## Açık sorular

Yok. Karar 4 (anahtarı kapatanın takvimindeki kayıtlar güncellenmeye devam eder) planla onaylandı.
