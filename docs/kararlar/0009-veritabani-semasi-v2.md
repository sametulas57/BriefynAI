# ADR-0009: Veritabanı şeması v2 (baştan, tek migration)

- Durum: Kabul edildi · Tarih: 2026-10-21

## Bağlam
Tarama (iç proje notu): konu/kararlar tek kolonda JSON, profil claim tablosunda, kullanıcıya giden FK'ler eksik, kısıtlar yalnız kodda, isimler karışık, davet token'ı düz metin ve loglarda, uygulama kullanıcısı şema değiştirebiliyor. Ulaş: veri silinebilir, sağlam ve genişletilebilir olsun.

## Karar
- Tek `InitialCreate`; tablolar: `AspNetUsers` (+`FullName`, `AvatarFileName`, `CreatedAtUtc`), `Workspaces`, `WorkspaceMembers`, `WorkspaceInvitations`, `Meetings`, `MeetingTranscripts`, `MeetingPoints`, `ActionItems`, `Comments`, `CommentReactions`, `Logs`.
- Konu ve kararlar tek tabloda (`MeetingPoints.Kind`); kaydın ve maddenin kaynağı kolonla (`Meetings.Source`, `*.Source`: AI/elle). Ulaş, 2026-10-21.
- Kısıtlar veritabanında: benzersiz tepki `(CommentId, UserId)`, kişisel alan kullanıcı başına bir, telefon tekliği, davet `TokenHash` tekliği, sayı kolonlarına (rol, tür, kaynak, durum) kontrol.
- Ekip silinince toplantılar, maddeler, görevler, yorumlar, davetler ve üyelikler veritabanında zincirleme silinir. Kullanıcıya giden FK'ler (yazar, atanan, tamamlayan, davet eden) zincirleme değil: SQL Server aynı tabloya birden çok zincirleme yolu kabul etmiyor. Uygulamada hesap silme yok; eklenirse o iş bu bağları önce temizler.
- `Meetings` ve `ActionItems`'ta `RowVersion`: aynı anda iki değişiklikte sonuncusu sessizce kazanmaz.
- Davet bağlantısında rastgele token, veritabanında yalnız SHA-256 özeti.
- Şema kurulumu ayrı adım: `migrate` servisi (`briefyn_migrator`, şema yetkili) EF migration'ı, Hangfire tablolarını kurar ve çıkar; `web` (`briefyn_app`) yalnız okur/yazar.
- Log: veritabanına yalnız uyarı ve hata; ASP.NET ve EF ayrıntısı (istek adresleri dahil) yalnız uyarıdan itibaren; 30 günden eski log her gün silinir.
- Hangfire işleri yalnız kimlik ya da şifreli değer taşır: analiz işi toplantı numarası, e-posta işi toplantı numarası + şifreli alıcı adresi (ekip dışı alıcının kullanıcı numarası olmadığı için).

## Sonuçlar
Eski veriler silindi (yedek `~/Yedekler/briefyn-db-2026-10-21/`). Yeni şema değişikliği yeni migration'la, `migrate` servisiyle uygulanır. ADR-0006'daki "uygulama şema değiştirebilir" kısmının yerini aldı.

## Kaynak
iç proje notu, iç proje notu
