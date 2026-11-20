# Spec: Görevleri takvime aktarma (Backlog 5, ilk adım)

Durum: onaylandı (Ulaş, 2026-11-20) · plan `tasks/plan.md`, görevler `tasks/todo.md`

## Amaç

Toplantıdan çıkan, bir kişiye atanmış ve son tarihi olan görev, o kişinin takvimine girsin. Kişi son tarihi gününü planladığı yerde, gecikmeden önce görsün. Bugün Briefyn son tarihten önce hiçbir şey göndermiyor; tek hatırlatma tarih geçince giden "Geciken görevlerin var" e-postası.

- Ana hedef Outlook / Microsoft 365 (Ulaş, 2026-11-20). Google ve öteki takvimler `.ics` dosyasıyla, ikinci planda.
- Hesap bağlama yok (Microsoft Graph yayından sonra). Her şey mevcut e-posta (SMTP) altyapısıyla gidiyor.
- Kapsam yalnız takvim. Slack/Teams'e özet ve Trello/Jira'ya aksiyon, Backlog 5'in sonraki adımları; her biri ayrı spec olacak.

### Kullanıcı hikâyeleri

1. Ayşe'ye toplantı sayfasında "28 Kasım" son tarihli bir görev atanıyor. Ayşe'ye bir takvim daveti geliyor; Outlook'ta Kabul et dediğinde görev 28 Kasım'a tüm gün olarak yerleşiyor.
2. Son tarih 2 Aralık'a çekiliyor. Ayşe'nin takvimindeki kayıt yeni bir kayıt açmadan 2 Aralık'a taşınıyor.
3. Görev bitti işaretleniyor, ataması kaldırılıyor, son tarihi siliniyor ya da toplantı siliniyor. Ayşe'ye iptal geliyor; kayıt takviminden kalkıyor.
4. Görev Ayşe'den Mehmet'e geçiyor. Ayşe'ye iptal, Mehmet'e yeni davet gidiyor.
5. Ayşe daveti silmiş ya da başka bir takvim kullanıyor. "Bana Atananlar" sayfasında görevin yanındaki "Takvimime gönder" düğmesiyle daveti yeniden alıyor ya da "Takvim dosyasını indir" ile `.ics` dosyasını indiriyor.
6. Ayşe otomatik davet istemiyor. Profil sayfasındaki anahtarı kapatıyor; artık kendiliğinden davet gitmiyor, düğme yine çalışıyor.

### Alınan kararlar (Ulaş, 2026-11-20)

- Yol: e-postayla takvim daveti (`METHOD:REQUEST`) + `.ics` indirme yedek. Outlook web bağlantısı yok (resmi belgesi yok).
- Tetik: ikisi birden. Atanınca otomatik gider, düğmeyle yeniden gönderilebilir.
- Değişiklik: takvimdeki kayıt güncellenir ya da iptal edilir. Bunun için gönderilen davetler küçük bir tabloda tutulur (şema değişikliği onaylandı).
- Briefyn'e bağlantı: yeni bir ayar (`App:PublicUrl`) eklenir. Ayar boşsa davette bağlantı olmaz. Site herkese açık adrese çıkınca doldurulur (Backlog 1'e eklendi).
- Ekipten çıkan ya da çıkarılan kişiye, o ekipteki görevleri için iptal gider.
- Davet, kim hangi Outlook'u kullanırsa kullansın çalışmalı: klasik masaüstü, yeni Outlook, web, mobil; kişisel ve iş hesabı.

### Varsayımlar (onayda düzeltilebilir)

1. Takvim kaydı son tarih gününe **tüm gün** olarak yerleşir; `ActionItem.DueDate` saat tutmuyor (`DateOnly`).
2. Davette bir hatırlatma olur: bir gün önce saat 09:00 (Türkiye saati).
3. Otomatik davet varsayılan olarak **açık**, geciken görev hatırlatması gibi. Profil'de ayrı bir anahtarla kapatılır.
4. Son tarihi bugünden önce olan görev için otomatik davet gitmez; onu geciken görev hatırlatması karşılar. Düğme yine çalışır.
5. Davet yalnız görevin atandığı kişiye gider. Düğme ve `.ics` indirme yalnız o kişiye görünür ve yalnız onun için çalışır.
6. Düzenleyen (ORGANIZER) Briefyn'in gönderici adresi, katılımcı görevli kişi. Yanıt istenmez (`RSVP=FALSE`); tahminim, Outlook yine de "yanıt gönder" sorabilir. Elle denemede bakılacak.
7. Davetin başlığı görev metni, açıklaması toplantı adı ve tarihi; `App:PublicUrl` doluysa "Bana Atananlar" sayfasının bağlantısı.
8. Ekipten çıkan kişiye giden iptal görev metnini taşımaz, yalnız kaydı kaldırır. ADR-0005'e göre ayrılan kişiye o ekibin görev içeriği ulaşmamalı.

## Teknoloji

Mevcut yığın, yeni paket yok:

- ASP.NET Core Razor Pages, `net10.0`, EF Core 10 + SQL Server, Hangfire 1.8.
- E-posta: `System.Net.Mail` + `EmailSettings` (mevcut `EmailService` ile aynı SMTP).
- `.ics` metni elle üretilir (RFC 5545'in gereken küçük alt kümesi). Bunun için bir kütüphane eklenmez.

## Komutlar

```
Doğrulama (lint, biçim, tip, build, testler):  scripts/verify.sh
Yalnız bu işin testleri:                       dotnet test MeetingWeb.Tests --nologo --filter "FullyQualifiedName~Calendar"
Migration:                                     dotnet ef migrations add AddCalendarInvites --project MeetingWeb
Yerelde çalıştırma:                            docker compose up -d --build
```

Commit öncesi hook ve CI `scripts/verify.sh`'yi koşar.

## Proje yapısı

```
MeetingWeb/Models/CalendarInvite.cs                 → gönderilen davetin kaydı (görev, kişi, UID, sıra no, gönderilen tarih, durum)
MeetingWeb/Services/CalendarInviteBuilder.cs        → .ics metnini üretir (saf, statik, test edilir)
MeetingWeb/Services/CalendarSyncService.cs          → görevin şimdiki hâliyle gönderilmiş daveti karşılaştırır, davet/güncelleme/iptal kararını verir
MeetingWeb/Services/IEmailService.cs, EmailService.cs → yeni SendCalendarInviteAsync (text/calendar parçalı e-posta)
MeetingWeb/Pages/Details.cshtml.cs                  → atama, son tarih, bitti/açık değişince eşitleme işi kuyruğa
MeetingWeb/Pages/MyActions.cshtml(.cs)              → "Takvimime gönder" ve "Takvim dosyasını indir"
MeetingWeb/Pages/History.cshtml.cs                  → toplantı silinmeden önce iptaller kuyruğa
MeetingWeb/Pages/Workspace/Members.cshtml.cs        → üye çıkınca/çıkarılınca iptaller kuyruğa
MeetingWeb/Pages/Profile/Index.cshtml(.cs)          → "Bana atanan görevleri takvim daveti olarak gönder" anahtarı
MeetingWeb/Models/ApplicationUser.cs                → CalendarInvitesEnabled (varsayılan açık)
MeetingWeb/appsettings.json                         → App:PublicUrl (boş; site herkese açık adrese çıkınca doldurulur)
MeetingWeb/Migrations/                              → AddCalendarInvites
MeetingWeb.Tests/Features/CalendarInviteTests.cs    → davranış testleri
MeetingWeb.Tests/Security/WorkspaceBoundaryTests.cs → yeni uç noktalar listeye eklenir
docs/decisions/0015-takvim-daveti.md                → karar kaydı (ADR-0008'deki "Takvime ekleme (.ics) ayrı iş" notunu kapatır)
```

Dosya adları plana göre değişebilir; ilke değişmez: mantık `Services/`'te, sayfalar yalnız kuyruğa iş atar.

## Tasarım ilkeleri

- **Tek giriş noktası, eşitleme mantığı.** Görevle ilgili her değişiklikte aynı Hangfire işi kuyruğa girer: `SyncAsync(actionItemId)`. İş, görevin şimdiki hâlini (atanan, son tarih, bitti mi, kişinin tercihi, ekip üyeliği) gönderilmiş davetle karşılaştırır ve gerekeni yapar: hiçbir şey, davet, güncelleme (aynı UID, `SEQUENCE` bir artar) ya da iptal (`METHOD:CANCEL`). Aynı iş iki kez koşarsa ikincisi fark bulmaz, e-posta gitmez.
- **Kuyruğa yalnız kimlik gider.** Hangfire'a e-posta adresi ya da görev metni yazılmaz (ADR-0009). Metin, iş koşarken veritabanından okunur.
- **Toplantı silinmesi.** Görev satırı silinince eşitleme işi görevi bulamaz. Davet kaydı görev satırına bağlı silinmez (yabancı anahtar silme zinciri yok), iptal için gereken UID ve sıra no orada kalır; iptal gidince kayıt silinir.
- **Kaçış.** Görev metni yapay zekâdan ya da kullanıcıdan gelir. `.ics` içinde `\`, `;`, `,` ve satır sonları kaçırılır; satırlar 75 baytta katlanır, satır sonu CRLF. E-posta başlığına satır sonu girmez (S8 kuralı).
- **Düğme sınırı.** "Takvimime gönder" e-posta gönderdiği için kişi başı sınır var (paylaş düğmesindeki gibi; sayı planda).

### Kod biçimi

Mevcut `EmailService.BuildSummaryHtml` gibi, metni üreten kısım saf ve statik; testler doğrudan çağırır:

```csharp
public static class CalendarInviteBuilder
{
    // METHOD:REQUEST davet ve güncelleme, METHOD:CANCEL iptal. Tüm gün: DTSTART;VALUE=DATE, DTEND ertesi gün.
    public static string Build(CalendarInviteData invite, CalendarMethod method)
    {
        var ics = new IcsWriter();
        ics.Line("BEGIN:VCALENDAR").Line("VERSION:2.0").Line("PRODID:-//Briefyn AI//Görevler//TR")
           .Line("METHOD:" + (method == CalendarMethod.Cancel ? "CANCEL" : "REQUEST"))
           .Line("BEGIN:VEVENT")
           .Line("UID:" + invite.Uid)
           .Line("SEQUENCE:" + invite.Sequence)
           .Line("DTSTART;VALUE=DATE:" + invite.DueDate.ToString("yyyyMMdd"))
           .Line("DTEND;VALUE=DATE:" + invite.DueDate.AddDays(1).ToString("yyyyMMdd"))
           .Text("SUMMARY", invite.TaskText);   // Text() kaçırır ve katlar
        // ...
    }
}
```

Kod ve yorumlar İngilizce; kullanıcıya görünen her metin sade Türkçe, teknik terim yok ("davet", "takvim dosyası"; "ICS", "UID" değil).

## Test stratejisi

xUnit, `MeetingWeb.Tests` (mevcut `BriefynFactory`, `EmailServiceOverride` ile e-posta yakalanır).

**Birim (`CalendarInviteBuilder`):**
- Tüm gün tarih alanları, `METHOD`, `UID`, `SEQUENCE` doğru.
- Görev metnindeki `;`, `,`, `\`, satır sonu kaçırılıyor; uzun Türkçe metin UTF-8 bayt sınırında, harf ortasından bölünmeden katlanıyor.
- İptal aynı UID'yi ve daha büyük `SEQUENCE`'ı taşıyor.

**Davranış (`CalendarSyncService` + sayfalar):**
- Tarihli atama → bir davet; aynı iş ikinci kez → e-posta yok.
- Tarih değişti → güncelleme, sıra no arttı. Atama kaldırıldı / tarih silindi / bitti → iptal. Yeniden açıldı → yeni davet.
- Başkasına geçti → eskiye iptal, yeniye davet.
- Toplantı silindi → iptal. Ekipten çıktı → iptal, yeni davet yok.
- Tercih kapalı → otomatik davet yok, düğme çalışıyor. Son tarih geçmiş → otomatik davet yok.
- Hangfire'a giden iş argümanlarında e-posta adresi ya da görev metni yok.

**Güvenlik:**
- Düğme ve `.ics` indirme, başkasına atanmış görevde ve başka ekibin görevinde çalışmıyor; yeni uç noktalar `WorkspaceBoundaryTests` listesinde (yoksa test kırılır).
- Düğme sınırı aşılınca e-posta gitmiyor.

**Elle (otomatik test edilemez, iş bitmeden yapılır):** gerçek e-postayla her Outlook'ta üç adım denenir: davet Kabul et ile takvime giriyor mu, güncelleme aynı kaydı taşıyor mu, iptal kaydı kaldırıyor mu. Ayrıca Gmail'de davet ve `.ics` dosyası. Sonuç `docs/raporlar/`'a kısa not olarak yazılır. Backlog'daki araştırma bunları belgeden söylüyor, bu projede henüz denenmedi.

| Outlook | Hesap | Kim dener |
|---|---|---|
| Outlook web | kişisel (Ulaş) | Claude, yerleşik tarayıcıda; giriş ve Kabul et onayı Ulaş'tan |
| Yeni Outlook (Mac) | kişisel (Ulaş) | Claude, bilgisayar kontrolüyle açılabilirse; açılamazsa Ulaş |
| Outlook mobil | kişisel (Ulaş) | Ulaş (telefona Claude erişemez) |
| Gmail (web) | herhangi | Claude ya da Ulaş |
| Klasik masaüstü Outlook (Windows) | — | denenemiyor, cihaz yok |
| İş hesabı (Microsoft 365, kurumsal) | — | denenemiyor, hesap yok |

Denenemeyen iki satır bilinen risk olarak rapora yazılır. Davet standart biçime (RFC 5545 / iTIP) uyduğu için çalışması beklenir; tahminim, kontrol edilmedi. İş hesabında kuruluşun dış davetleri süzmesi ayrı risk. Yayından sonra ilk kurumsal kullanıcıdan geri bildirim istenir.

## Sınırlar

- **Her zaman:** `scripts/verify.sh` geçmeden commit yok; yeni uç nokta `WorkspaceBoundaryTests`'e eklenir; e-posta adresi ve görev metni Hangfire'a, loglara yazılmaz; kullanıcıya görünen metin sade Türkçe.
- **Önce sor:** onaylanan `CalendarInvites` tablosu ve `ApplicationUser.CalendarInvitesEnabled` dışında şema değişikliği; yeni paket; e-posta sağlayıcısını ya da gönderici adresini değiştirmek; Microsoft Graph ya da hesap bağlama.
- **Asla:** kişiye haber vermeden başka adrese davet (yalnız görevli kişi); Outlook web bağlantısı (bu adımda reddedildi); yapay zekâ sağlayıcısına yeni veri göndermek (bu işin yapay zekâyla ilgisi yok).

## Başarı ölçütleri

1. Tarihli atanan görev için görevli kişiye tek bir davet e-postası gidiyor. Elle denenebilen her Outlook'ta (web, yeni Outlook, mobil) Kabul et ile son tarih gününe tüm gün kayıt oluşuyor; denenemeyenler raporda risk olarak yazılı.
2. Son tarih değişince aynı kayıt yeni tarihe taşınıyor; ikinci bir kayıt oluşmuyor.
3. Bitti, atama kaldırma, tarih silme, toplantı silme ve ekipten çıkmada kayıt iptal ediliyor.
4. Başkasına geçen görevde eski kişiye iptal, yeni kişiye davet gidiyor.
5. Profil'de kapatan kişiye otomatik davet gitmiyor; "Takvimime gönder" düğmesi çalışıyor.
6. "Takvim dosyasını indir" ile inen dosya Outlook ve Google Takvim'de açılıyor.
7. Başka kişinin ya da başka ekibin görevi için düğme ve indirme reddediliyor.
8. `scripts/verify.sh` GEÇTİ; elle deneme notu `docs/raporlar/`'da; ADR-0015 yazıldı; Backlog 5'te takvim adımı çıkarıldı, Slack/Teams ve Trello/Jira kaldı.

## Açık sorular

Yok. Onayla kapananlar:

1. Elle denemenin paylaşımı, test stratejisindeki tablodaki gibi.
2. Bu iş, `deneme/azure-konusmaci` dalındaki konuşmacı işi main'e girdikten sonra main'den başlar.
