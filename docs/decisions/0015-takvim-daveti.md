# ADR-0015: Görevleri takvime aktarma: e-postayla takvim daveti

- Durum: Kabul edildi · Tarih: 2026-11-20

## Bağlam
Atanmış ve son tarihli görev, kişinin takvimine girmeli; Briefyn son tarihten önce hiçbir şey göndermiyordu (tek hatırlatma, tarih geçince giden e-posta). ADR-0008 "Takvime ekleme (.ics) Ulaş söyleyince ayrı iş" diyordu; bu ADR o işi kapatır. Ana hedef Outlook / Microsoft 365 (Ulaş, 2026-11-20). Araştırma (Backlog 5, 2026-11-20): yalnız `.ics` indirme Outlook'ta yetmiyor (yeni Outlook, web ve mobilde elle "dosyadan yükle" gerekiyor); Outlook web "takvime ekle" bağlantısının resmi belgesi yok; hesap bağlama (Microsoft Graph) kurumsal hesapta yönetici onayı istiyor. Ayrıntı `SPEC.md` ve `tasks/plan.md`.

## Karar
- **Yol:** mevcut SMTP ile e-postayla takvim daveti (iCalendar / iTIP, `METHOD:REQUEST`, iptal `METHOD:CANCEL`). Mail düz metin + `text/calendar; method=...; charset=UTF-8` alternatif parçası; ek yok. Yedek olarak "Bana Atananlar"da takvim dosyası indirme (`METHOD:PUBLISH`, aynı UID). Outlook web bağlantısı ve hesap bağlama yok. Yeni paket yok; `.ics` metni `CalendarInviteBuilder`'da elle üretilir (kaçış, 75 baytta UTF-8'e uygun katlama, CRLF).
- **Kayıt:** tüm gün (`DTSTART;VALUE=DATE`), hatırlatma bir gün önce 09:00 (`TRIGGER:-PT15H`), `TRANSP:TRANSPARENT`, yanıt istenmez (`RSVP=FALSE`). Düzenleyen Briefyn'in gönderici adresi.
- **Eşitleme:** görevle ilgili her değişiklik yalnız görev kimliğiyle tek bir Hangfire işi kuyruğa atar: `ICalendarSyncService.SyncAsync(görevId)` (ADR-0009: işe e-posta adresi ya da metin yazılmaz). İş, görevin şimdiki hâlini gönderilmiş davetle karşılaştırır: hiçbir şey, davet, güncelleme (aynı UID, `SEQUENCE` bir artar) ya da iptal. İkinci koşu fark bulmaz.
- **Davet kaydı:** `CalendarInvites` (görev, kişi, UID, sıra no, takvimdeki tarih, durum, "gönderilecek" işareti, `RowVersion`). Göreve yabancı anahtar yok: toplantı silinse de iptal için gereken UID ve sıra no kalır, iptal gidince satır silinir. Kişiye yabancı anahtar silme zincirli: hesap silinince satırları da gider, silinen hesaba e-posta gitmez.
- **Gönderim sırası:** önce satır "gönderilecek" işaretiyle ve yeni sıra noyla kaydedilir, sonra e-posta gider, sonra işaret kalkar. İşaretli satır 10 dakika "yolda" sayılır, başka koşu ona dokunmaz. Gönderim hata verirse sahiplik bırakılır, Hangfire'ın yeniden denemesi aynı sıra noyla gönderir. Aynı anda iki iş tek e-posta gönderir.
- **İptal içeriği:** iptal görev metnini, açıklamayı, bağlantıyı taşımaz ("İptal edilen görev"). ADR-0005: ekipten çıkan kişiye o ekibin görev içeriği ulaşmamalı.
- **Kime:** yalnız görevin atandığı, hâlâ ekipte olan, hesabını silmeyi istememiş kişiye. Görev başkasına geçerse eskiye iptal, yeniye davet; gönderimi başarısız olmuş eski kayda da iptal gider (davet ulaşmış olabilir).
- **Tercih:** `ApplicationUser.CalendarInvitesEnabled`, varsayılan açık (mevcut kullanıcılar dahil). Kapalıysa yeni otomatik davet gitmez; takvimde duran kayıtlar güncellenmeye ve iptal edilmeye devam eder (plan karar 4, Ulaş onayı). Son tarihi geçmiş göreve otomatik davet gitmez.
- **Kişinin kendi kontrolü:** "Takvimime gönder" daveti değişiklik olmasa da sıra noyu artırarak yeniden yollar (tercih ve geçmiş tarih engellemez); kişi başı saatte 5 (`EmailSettings:CalendarResendLimitPerHour`). Konusu "Takviminiz için yeniden: …", ilk davetten ayırt edilsin diye (Ulaş). İptal edilmiş bir kayıt yeniden etkinleşirken sıra no iptalin önüne geçer.
- **Tetikler:** toplantı sayfasında atama, son tarih, bitti/açık; Bana Atananlar'da tamamlandı/geri al; toplantı silme (davetli görevler silmeden önce bulunur); üye çıkarma ve ekipten ayrılma. Ekip silme ve kuyruğa alınamayan işler için günlük süpürme (`calendar-sweep`, Türkiye saatiyle 04:00) etkin ya da gönderilecek her kaydı yeniden eşitler.
- **Bağlantı:** `App:PublicUrl` doluysa davette "Bana Atananlar" bağlantısı olur; bugün boş, site herkese açık adrese çıkınca doldurulur (Backlog 1).

## Sonuçlar
- Outlook mobilde (iOS, kişisel hesap) davet, güncelleme ve iptal elle denendi, çalıştı (`docs/raporlar/2026-11-20-takvim-daveti-deneme.md`). Klasik masaüstü Outlook ve kurumsal hesap denenemedi; standarda uyduğu için çalışması bekleniyor, kanıt yok.
- Outlook kabul edilen davet mailini ve mevcut kayda gelen güncellemeyi kendisi kaldırıp Silinmiş Öğeler'e koyuyor; kullanıcı "mail gelmedi" sanabilir.
- Kuyruk o an çalışmazsa, takvimde kaydı olmayan yeni bir atamanın daveti görev bir daha değişene kadar gitmez (süpürme yalnız mevcut kayıtlara bakar). Bir davet gönderilip son veritabanı adımı tam o anda başarısız olursa aynı davet 10 dakika sonra bir kez daha gidebilir.
- Hesap silme isteği ile silme arasındaki 7 günde, kişinin takvimindeki görevler için iptal gider (artık hedef değil).
- Görev metni değişince takvim güncellenmez; yalnız atama, tarih ve bitti durumu izlenir.
- Microsoft Graph ile hesap bağlama, Slack/Teams'e özet ve Trello/Jira'ya aksiyon ayrı işler (Backlog 5).

## Kaynak
- RFC 5545 (iCalendar), RFC 5546 (iTIP)
- `SPEC.md`, `tasks/plan.md`, `tasks/todo.md` (2026-11-20)
- Elle deneme: `docs/raporlar/2026-11-20-takvim-daveti-deneme.md`
