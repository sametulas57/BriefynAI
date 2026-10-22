# ADR-0013: Toplantı serileri ve Zoom numarasının anahtarlı özeti

- Durum: Kabul edildi · Tarih: 2026-10-22

## Bağlam
Tekrar eden toplantılar (haftalık ekip toplantısı, sprint) bir seri altında toplanacak, gündem serinin verisinden hesaplanacak (iç proje notu). Seri elle açılır; Zoom'dan alınan tekrar eden toplantıda kendiliğinden. Zoom'da tekrar eden toplantının her oturumu aynı toplantı numarasını taşır; seriyi bulmak için numarayı saklamak gerekir. Numarayı bilen toplantıya katılmayı deneyebilir, bu yüzden açık saklanmaz.

Zoom dokümanı ([Meetings API](https://developers.zoom.us/docs/api/meetings/), "Get meeting recordings" ve "List all recordings") 2026-10-22'de okundu:
- `id`: tamsayı, "toplantı numarası" (meeting number). 9–11 hane; `long` olarak okunur.
- `type`: şemada metin (`"1"`…`"99"`), örnek cevaplarda sayı (`"type": 1`). İkisi de okunur.
- Değerler: 1 anlık, 2 planlı, **3 sabit saatsiz tekrar eden**, **4 kişisel oda numarası (PMI)**, 5/6/9 webinar, 7 kişisel sesli konferans, **8 sabit saatli tekrar eden**, 96 My Notes, 99 portaldan yüklenen.

## Karar
- **Varlık:** `MeetingSeries` (ekip, şifreli ad `MeetingSeries.Name` ≤ 100, `ZoomMeetingKey` boş olabilir, oluşturan, tarih). `Meetings.SeriesId` boş olabilir. Bir toplantı en fazla bir seride; seri ekibe aittir (ADR-0005).
- **Silme yolu:** `Meetings.SeriesId` yabancı anahtarı `NoAction`; seri silinince toplantıların bağını servis boşaltır (aynı işlemde). Ekip silinince seriler ekiple silinir (cascade), toplantılar da zaten ekiple silindiği için çoklu silme yolu doğmaz. SQL Server'ın "multiple cascade paths" hatası bu yüzden yok.
- **Yetki:** ekibin her üyesi seri açar, toplantıyı bağlar/çıkarır. Yeniden adlandırma ve silme: seriyi açan, kurucu ya da yönetici (`WorkspaceMember.RunsTeam`). Toplantı yalnız kendi ekibinin serisine bağlanır; başka ekibin serisi "yok" (404) gibi davranır.
- **Zoom'dan seri:** yalnız tür 3 ve 8. Tür 4 (kişisel oda numarası her toplantıda aynı, alakasız toplantıları bağlardı) ve diğerleri seri açmaz. Ekipte bu numaranın serisi varsa toplantı ona bağlanır, yoksa Zoom konusuyla seri açılır.
- **Numaranın özeti:** `ZoomMeetingKey` = hex(HMAC-SHA256(k, "zoom:" + numara)). Düz SHA-256 yetmez: numara uzayı küçük (≤ 10¹¹), denenerek bulunur. `k`, şifreleme anahtarından (`ENCRYPTION_KEY_V2`, numara 1) HKDF-SHA256 ile `Briefyn.MeetingSeries.ZoomMeetingKey` etiketiyle türetilir: yeni sır yok, `.env` değişmez, şifreleme anahtarıyla aynı yerde korunur, ama şifreleme için kullanılan anahtarın kendisi değildir. Değer yalnız eşitlik için kullanılır; `(WorkspaceId, ZoomMeetingKey)` eşsiz (boşlar hariç). Numara hiçbir kolona, loga, işe açık yazılmaz.
- **Anahtar değişimi:** şifreleme anahtarı değişirse özet türetmesi 1 numaralı anahtardan sürer (okuma için zaten tutulur). Türetme değiştirilirse eski seriler Zoom'la eşleşmez; bir sonraki oturum yeni seri açar (veri kaybı yok, toplantılar elle taşınır).

## Sonuçlar
- Eski Zoom toplantıları geriye dönük seriye bağlanamaz (numara saklanmamıştı).
- Kişi Zoom toplantısını seriden çıkarırsa bir sonraki oturum yine seriye bağlanır.
- Elle açılmış seriyle Zoom serisini birleştirme bu sürümde yok.

## Kaynak
iç proje notu
