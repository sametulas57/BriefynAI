# ADR-0005: Yetki: workspace üyeliği tek kapı

- Durum: Kabul edildi · Tarih: 2026-10-17

## Bağlam
İlk incelemede başka kullanıcının toplantısı ID ile açılabiliyordu (IDOR), bildirimler herkese gidiyordu.

## Karar
Toplantı erişimi `MeetingAccessService` ve aktif workspace `ActiveWorkspaceService` üzerinden; her handler yetki kontrolü yapar. SignalR hub [Authorize], bildirim yalnız workspace üyelerine. Yetki testleri koddan önce yazılır.

## Sonuçlar
Yeni her uç nokta bu servisleri kullanmalı; testsiz yetki kodu kabul edilmez.

Ek (2026-11-20): kural artık kodda zorunlu. `MeetingWeb.Tests/Security/WorkspaceBoundaryTests.cs` sayfa işleyicilerini, hub metotlarını ve sayfa dışı uç noktaları yansımayla bulur; her biri kayıt listesinde ya başka workspace'e erişemediğini kanıtlayan bir teste bağlanır ya da gerekçeyle "yabancı girdi yok" / "workspace dışı" diye işaretlenir. Kaydedilmemiş yeni uç nokta testi kırar. EF genel sorgu filtresi (`HasQueryFilter`) seçilmedi: kullanıcısız arka plan işleri her yerde istisna isterdi ve filtre yazmaları korumaz.

## Kaynak
iç proje notu
