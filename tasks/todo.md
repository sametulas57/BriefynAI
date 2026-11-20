# Görevler: Görevleri takvime aktarma

Plan: `tasks/plan.md` · Spec: `SPEC.md`. Odaklı test komutu: `dotnet test MeetingWeb.Tests --nologo --filter "FullyQualifiedName~Calendar"`. Her görev commit'ten önce `scripts/verify.sh`'den (hook) geçer.

## Aşama 1: Atamadan Outlook'a ilk dilim

- [x] **Görev 1: Takvim metnini üreten parça** (S)
  - Kabul: davet ve iptal metni üretiliyor; tüm gün tarih, UID, sıra no, düzenleyen, katılımcı (`RSVP=FALSE`), bir gün önce 09:00 hatırlatma var. `\ ; ,` ve satır sonu kaçırılıyor; satırlar 75 baytta, Türkçe harfin ortasından bölünmeden katlanıyor; satır sonu CRLF. İptal metni görev metnini taşımıyor (spec varsayım 8).
  - Doğrula: odaklı test.
  - Bağımlılık: yok.
  - Dosyalar: `MeetingWeb/Services/CalendarInviteBuilder.cs`, `MeetingWeb.Tests/Features/CalendarInviteBuilderTests.cs`.

- [x] **Görev 2: Davet tablosu, kişi tercihi, site adresi ayarı** (M)
  - Kabul: `CalendarInvites` tablosu (görev kimliği yabancı anahtarsız, kişi, UID, sıra no, gönderilen tarih, durum, "gönderilecek" işareti, `RowVersion`; (görev, kişi) tekil); `ApplicationUser.CalendarInvitesEnabled` varsayılan açık; `App:PublicUrl` ayarı boş değerle ve bir seçenek sınıfıyla.
  - Doğrula: migration temiz uygulanıyor; `SchemaTests` ve build geçiyor.
  - Bağımlılık: yok.
  - Dosyalar: `MeetingWeb/Models/CalendarInvite.cs`, `MeetingWeb/Models/ApplicationUser.cs`, `MeetingWeb/Data/ApplicationDbContext.cs`, `MeetingWeb/Migrations/*_AddCalendarInvites.cs`, `MeetingWeb/appsettings.json`.

- [x] **Görev 3: Eşitleme servisi ve davet e-postası** (M)
  - Kabul: `SyncAsync(actionItemId)` plan karar 1, 3, 4, 7'ye göre davet / güncelleme / iptal gönderiyor ya da hiçbir şey yapmıyor; ikinci koşu e-posta göndermiyor; geçmiş tarihe otomatik davet yok. `SendCalendarInviteAsync` e-postası `text/calendar; method=REQUEST|CANCEL` parçası ve düz metin parçasıyla gidiyor; başlıkta satır sonu yok.
  - Doğrula: odaklı test (davranış tablosundaki her durum + eşzamanlı iki koşu).
  - Bağımlılık: 1, 2.
  - Dosyalar: `MeetingWeb/Services/CalendarSyncService.cs`, `MeetingWeb/Services/IEmailService.cs`, `MeetingWeb/Services/EmailService.cs`, `MeetingWeb/Program.cs`, `MeetingWeb.Tests/Features/CalendarSyncTests.cs`.

- [x] **Görev 4: Toplantı sayfasından tetikleme** (S)
  - Kabul: `Details:PostAssignAction` ve `Details:PostToggleTask` başarılı kayıttan sonra eşitleme işini kuyruğa atıyor; kayıt başarısızsa atmıyor; iş argümanında yalnız görev kimliği var.
  - Doğrula: odaklı test (`FakeBackgroundJobClient.CountFor`).
  - Bağımlılık: 3.
  - Dosyalar: `MeetingWeb/Pages/Details.cshtml.cs`, `MeetingWeb.Tests/Features/CalendarSyncTests.cs`.

### Kontrol A: gerçek Outlook (devam etmeden önce)
- [x] `scripts/verify.sh` GEÇTİ (971e072'nin commit öncesi kontrolü, .NET 473/473).
- [x] Yerelde Docker yığınıyla gerçek SMTP üzerinden, Ulaş'ın kişisel adresine (2026-11-20, görev 7002):
  - Outlook mobil (iOS, kişisel hesap), Ulaş denedi: davet takvim daveti olarak tanındı ("28 Kasım, 1 gün", RSVP düğmesi), kabul edince takvime tüm gün girdi; son tarih 30 Kasım'a çekilince aynı kayıt taşındı, ikinci kayıt açılmadı (sıra no 1); bitti işaretlenince iptal geldi, kayıt kalktı (sıra no 2).
  - Outlook web: denenmedi (Ulaş'ın kararı, mobil yeterli görüldü). Yeni Outlook (Mac): denenmedi. İkisi rapora risk olarak yazılır (görev 9).
- [ ] Davet gereksiz klasörüne düşmedi: mail uygulamada görüldü, hangi klasörde olduğu kontrol edilmedi.
- [x] Biri bile geçmezse dur: üç adım da geçti, Aşama 2'ye geçilebilir.

## Aşama 2: Kişinin kendi kontrolü

- [x] **Görev 5: Bana Atananlar'da tetik, düğme ve dosya indirme** (M)
  - Kabul: `PostComplete` ve `PostReopen` eşitleme işini kuyruğa atıyor. Açık ve tarihli görevde "Takvimime gönder" (zorla gönderim, kişi başı sınır) ve "Takvim dosyasını indir" (`METHOD:PUBLISH`, aynı UID) var; ikisi de başkasının ve başka ekibin görevinde reddediliyor; yeni işleyiciler `WorkspaceBoundaryTests` listesinde.
  - Doğrula: odaklı test + `WorkspaceBoundaryTests`; inen dosya Outlook web'de ve Google Takvim'de açılıyor.
  - Bağımlılık: 3.
  - Dosyalar: `MeetingWeb/Pages/MyActions.cshtml`, `MeetingWeb/Pages/MyActions.cshtml.cs`, `MeetingWeb.Tests/Features/CalendarSyncTests.cs`, `MeetingWeb.Tests/Security/WorkspaceBoundaryTests.cs`.

- [x] **Görev 6: Profil'de otomatik davet anahtarı** (S)
  - Kabul: "Bana atanan görevleri takvim daveti olarak gönder" anahtarı kaydediliyor; kapalıyken yeni otomatik davet gitmiyor, düğme çalışıyor, mevcut kayıtlar güncelleniyor (plan karar 4).
  - Doğrula: odaklı test.
  - Bağımlılık: 3.
  - Dosyalar: `MeetingWeb/Pages/Profile/Index.cshtml`, `MeetingWeb/Pages/Profile/Index.cshtml.cs`, `MeetingWeb.Tests/Features/CalendarSyncTests.cs`.

### Kontrol B
- [x] `scripts/verify.sh` GEÇTİ (bf4ca0c, .NET 514/514; a3422bf, 515/515).
- [x] Ekranda (yerel, test hesabı, 2026-11-20): Bana Atananlar'da iki düğme masaüstünde görev bilgisinin altında, 375 piksel genişlikte alt alta; Profil'de anahtar ve açıklaması görünüyor; metinler sade Türkçe. Anahtar kapalıyken tarihli atamada davet gitmedi. İnen dosya `METHOD:PUBLISH`, katılımcı satırı yok, Türkçe harfler bölünmeden katlanmış, aynı kimlikle kayıt oluştu.
- [x] Outlook mobil (iOS, Ulaş): yeniden açılan görev için yeni davet geldi; "Takvimime gönder" iki kez gönderildi (sıra no 4, 5), Outlook ikisini de 30 Kasım'daki mevcut kayda işledi, ikinci kayıt açılmadı.
  - Not: Outlook kabul edilen davet mailini ve mevcut kayda gelen güncellemeyi kendisi kaldırıp Silinmiş Öğeler'e koyuyor; kullanıcı "mail gelmedi" sanabilir (Ulaş da öyle sandı). Rapora yazılır (görev 9).
  - Düğmeyle giden davetin konusu ayrı: "Takviminiz için yeniden: …" (a3422bf; Ulaş istedi, kalsın).

## Aşama 3: Görevin kaybolduğu yollar

- [x] **Görev 7: Toplantı silme ve ekipten çıkma** (M)
  - Kabul: `History:PostDelete` silmeden önce o toplantının davetli görevlerini bulup silmeden sonra eşitlemeyi kuyruğa atıyor → iptal gidiyor, satır siliniyor. `Members:PostRemoveMember` ve `PostLeaveWorkspace` o kişinin o ekipteki davetleri için eşitleme atıyor → iptal gidiyor, iptalde görev metni yok.
  - Doğrula: odaklı test.
  - Bağımlılık: 3.
  - Dosyalar: `MeetingWeb/Pages/History.cshtml.cs`, `MeetingWeb/Pages/Workspace/Members.cshtml.cs`, `MeetingWeb.Tests/Features/CalendarSyncTests.cs`.

- [x] **Görev 8: Günlük süpürme ve hesap silme** (S)
  - Kabul: her gün Türkiye saatiyle bir kez etkin davet kayıtları için eşitleme koşuyor (ekip silme ve ekip sahibinin hesap silmesiyle kaybolan görevlerin iptali bu yolla gidiyor). Hesap silmede o kişinin davet kayıtları siliniyor, ona e-posta gitmiyor.
  - Doğrula: odaklı test (ekip silindi → süpürme iptal gönderdi; hesap silindi → satır yok, e-posta yok).
  - Bağımlılık: 3.
  - Dosyalar: `MeetingWeb/Program.cs`, `MeetingWeb/Services/CalendarSyncService.cs`, `MeetingWeb/Services/AccountDeletionService.cs`, `MeetingWeb.Tests/Features/CalendarSyncTests.cs`.

- [x] **Görev 9: Belgeler** (S)
  - Kabul: `docs/decisions/0015-takvim-daveti.md` (ADR-0008'deki ".ics ayrı iş" notunu kapatır); `docs/raporlar/2026-11-..-takvim-daveti-deneme.md` (her Outlook için üç adımın sonucu, denenemeyenler risk olarak); Backlog 5'ten takvim adımı çıktı, Slack/Teams ve Trello/Jira kaldı; `docs/decisions/README.md` güncel.
  - Doğrula: belgeler okunuyor, bağlantılar doğru.
  - Bağımlılık: 1-8.
  - Dosyalar: `docs/decisions/0015-takvim-daveti.md`, `docs/decisions/README.md`, `docs/raporlar/…`, `Backlog.md`.

### Kontrol C: bitiş
- [x] `scripts/verify.sh` GEÇTİ (17fb5c1, .NET 535/535). [x] CI yeşil: 5bc0d28 (Tests, 536/536). İlk push kırmızıydı: test uygulamaları Linux dosya izleyici sınırını aşıyordu, 5bc0d28 düzeltti.
- Spec başarı ölçütleri (2026-11-20):
  - [x] 1. Tek davet; Kabul et ile tüm gün kayıt: Outlook mobilde geçti. Outlook web ve yeni Outlook denenmedi (Ulaş'ın kararı), raporda risk.
  - [x] 2. Tarih değişince aynı kayıt taşınıyor: Outlook mobilde geçti.
  - [x] 3. Bitti, atama kaldırma, tarih silme, toplantı silme, ekipten çıkma iptal ediyor: hepsi testte; gerçek Outlook'ta yalnız "bitti" denendi.
  - [x] 4. Başkasına geçince eskiye iptal, yeniye davet: testte.
  - [x] 5. Profil'de kapatana otomatik davet yok, düğme çalışıyor: testte ve yerelde; düğme Outlook mobilde.
  - [ ] 6. İnen dosya Outlook ve Google Takvim'de açılıyor: dosyanın içeriği yerelde doğrulandı, takvime yükleme denenmedi.
  - [x] 7. Başkasının ya da başka ekibin görevinde düğme ve indirme reddediliyor: WorkspaceBoundaryTests.
  - [x] 8. verify GEÇTİ; deneme notu `docs/raporlar/2026-11-20-takvim-daveti-deneme.md` (yerel); ADR-0015 yazıldı; Backlog 5'te takvim adımı çıktı.
- [ ] Elle deneme tablosu: yalnız Outlook mobil dolu; web, yeni Outlook ve Gmail denenmedi.
- [ ] `/agent-skills:review` koşulmadı (Ulaş'a hatırlatıldı).
