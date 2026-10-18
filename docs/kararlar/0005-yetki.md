# ADR-0005: Yetki: workspace üyeliği tek kapı

- Durum: Kabul edildi · Tarih: 2026-10-17

## Bağlam
İlk incelemede başka kullanıcının toplantısı ID ile açılabiliyordu (IDOR), bildirimler herkese gidiyordu.

## Karar
Toplantı erişimi `MeetingAccessService` ve aktif workspace `ActiveWorkspaceService` üzerinden; her handler yetki kontrolü yapar. SignalR hub [Authorize], bildirim yalnız workspace üyelerine. Yetki testleri koddan önce yazılır.

## Sonuçlar
Yeni her uç nokta bu servisleri kullanmalı; testsiz yetki kodu kabul edilmez.

## Kaynak
iç proje notu
