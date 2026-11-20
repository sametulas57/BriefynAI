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
- [ ] `scripts/verify.sh` GEÇTİ.
- [ ] Yerelde Docker yığınıyla gerçek SMTP üzerinden, Ulaş'ın kişisel adresine: atama → davet geldi, Kabul et ile takvime girdi; tarih değişti → aynı kayıt taşındı; bitti → kayıt kalktı. Outlook web'de Claude (giriş ve Kabul et onayı Ulaş'ta), mobilde ve yeni Outlook'ta Ulaş.
- [ ] Davet gereksiz klasörüne düşmedi.
- [ ] Biri bile geçmezse dur; e-posta biçimi düzeltilmeden Aşama 2'ye geçilmez.

## Aşama 2: Kişinin kendi kontrolü

- [ ] **Görev 5: Bana Atananlar'da tetik, düğme ve dosya indirme** (M)
  - Kabul: `PostComplete` ve `PostReopen` eşitleme işini kuyruğa atıyor. Açık ve tarihli görevde "Takvimime gönder" (zorla gönderim, kişi başı sınır) ve "Takvim dosyasını indir" (`METHOD:PUBLISH`, aynı UID) var; ikisi de başkasının ve başka ekibin görevinde reddediliyor; yeni işleyiciler `WorkspaceBoundaryTests` listesinde.
  - Doğrula: odaklı test + `WorkspaceBoundaryTests`; inen dosya Outlook web'de ve Google Takvim'de açılıyor.
  - Bağımlılık: 3.
  - Dosyalar: `MeetingWeb/Pages/MyActions.cshtml`, `MeetingWeb/Pages/MyActions.cshtml.cs`, `MeetingWeb.Tests/Features/CalendarSyncTests.cs`, `MeetingWeb.Tests/Security/WorkspaceBoundaryTests.cs`.

- [ ] **Görev 6: Profil'de otomatik davet anahtarı** (S)
  - Kabul: "Bana atanan görevleri takvim daveti olarak gönder" anahtarı kaydediliyor; kapalıyken yeni otomatik davet gitmiyor, düğme çalışıyor, mevcut kayıtlar güncelleniyor (plan karar 4).
  - Doğrula: odaklı test.
  - Bağımlılık: 3.
  - Dosyalar: `MeetingWeb/Pages/Profile/Index.cshtml`, `MeetingWeb/Pages/Profile/Index.cshtml.cs`, `MeetingWeb.Tests/Features/CalendarSyncTests.cs`.

### Kontrol B
- [ ] `scripts/verify.sh` GEÇTİ.
- [ ] Ekranda: düğme, indirme ve anahtar görünüyor, metinler sade Türkçe; düğmeyle gelen davet Outlook web'de mevcut kaydı güncelliyor, ikinci kayıt açmıyor.

## Aşama 3: Görevin kaybolduğu yollar

- [ ] **Görev 7: Toplantı silme ve ekipten çıkma** (M)
  - Kabul: `History:PostDelete` silmeden önce o toplantının davetli görevlerini bulup silmeden sonra eşitlemeyi kuyruğa atıyor → iptal gidiyor, satır siliniyor. `Members:PostRemoveMember` ve `PostLeaveWorkspace` o kişinin o ekipteki davetleri için eşitleme atıyor → iptal gidiyor, iptalde görev metni yok.
  - Doğrula: odaklı test.
  - Bağımlılık: 3.
  - Dosyalar: `MeetingWeb/Pages/History.cshtml.cs`, `MeetingWeb/Pages/Workspace/Members.cshtml.cs`, `MeetingWeb.Tests/Features/CalendarSyncTests.cs`.

- [ ] **Görev 8: Günlük süpürme ve hesap silme** (S)
  - Kabul: her gün Türkiye saatiyle bir kez etkin davet kayıtları için eşitleme koşuyor (ekip silme ve ekip sahibinin hesap silmesiyle kaybolan görevlerin iptali bu yolla gidiyor). Hesap silmede o kişinin davet kayıtları siliniyor, ona e-posta gitmiyor.
  - Doğrula: odaklı test (ekip silindi → süpürme iptal gönderdi; hesap silindi → satır yok, e-posta yok).
  - Bağımlılık: 3.
  - Dosyalar: `MeetingWeb/Program.cs`, `MeetingWeb/Services/CalendarSyncService.cs`, `MeetingWeb/Services/AccountDeletionService.cs`, `MeetingWeb.Tests/Features/CalendarSyncTests.cs`.

- [ ] **Görev 9: Belgeler** (S)
  - Kabul: `docs/decisions/0015-takvim-daveti.md` (ADR-0008'deki ".ics ayrı iş" notunu kapatır); `docs/raporlar/2026-11-..-takvim-daveti-deneme.md` (her Outlook için üç adımın sonucu, denenemeyenler risk olarak); Backlog 5'ten takvim adımı çıktı, Slack/Teams ve Trello/Jira kaldı; `docs/decisions/README.md` güncel.
  - Doğrula: belgeler okunuyor, bağlantılar doğru.
  - Bağımlılık: 1-8.
  - Dosyalar: `docs/decisions/0015-takvim-daveti.md`, `docs/decisions/README.md`, `docs/raporlar/…`, `Backlog.md`.

### Kontrol C: bitiş
- [ ] `scripts/verify.sh` GEÇTİ, CI yeşil.
- [ ] Spec'teki sekiz başarı ölçütü tek tek işaretlendi.
- [ ] Elle deneme tablosu dolu (web, yeni Outlook, mobil, Gmail).
- [ ] `/agent-skills:review` koşuldu (aşama "yayında değil": özellik bitince hatırlatılır).
