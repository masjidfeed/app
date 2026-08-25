---
name: maui-notifications
description: Domain knowledge for Masjid App notifications - offline local prayer/iqama and event reminders, and remote push via APNs (iOS) and FCM (Android) with token registration, categories, opt-in, and deep links. USE WHEN working on local notifications, prayer/iqama reminders, push notifications, device token registration, notification categories/preferences, permission rationale, or notification-tap deep linking (batches C1, C2; reminder reuse in B2). Trigger phrases include local notification, APNs, FCM, push, device token, notification category, opt-in/out, deep link, prayer reminder, iqama reminder, notification permission.
---

# Notifications - Local Reminders & Remote Push

Requirements: `FR-PT-10`, `FR-PN-01`–`FR-PN-07`, `FR-ST-02`, `NFR-SC-01`. Two distinct mechanisms -
keep them separate but share one permission flow and one deep-link router.

## Local notifications (offline reminders) - batch C1
Used for prayer/iqama and event reminders so they **fire without network** (`FR-PN-07`).
- Build a platform local-notification scheduling service (iOS `UNUserNotificationCenter` /
  Android `AlarmManager` + notification channels) behind a Core-facing interface.
- Per-prayer athan/iqama reminders with **configurable lead time** and **sound** (athan tone, beep, or
  silent/vibrate) (`FR-PT-10`).
- Schedule from computed times (salahapi-prayer-times skill) across the cached/offline window
  (e.g. the cached month). iOS caps pending notifications (~64) - schedule a rolling window and refill.
- **Reschedule** on: prayer-time data change, masjid switch, day rollover, and settings change.
- Reuse the same service for **per-event reminders** (batch B2) - do not build a second scheduler.

## Remote push - batch C2
- Integrate **APNs (iOS)** and **FCM (Android)**; obtain and refresh device tokens.
- **Register/update** the token with the Masjid API push endpoint (no auth in v1; payload contract
  matches [mock/config.json](../../../mock/config.json) `pushRegistrationUrl`). Re-register on token
  change/reinstall (`FR-PN-02`).
- **Categories** (`FR-PN-03`): prayer-time changes, new/updated events, community announcements. Map
  each push to a category and respect per-category opt-in (`FR-PN-04`, `FR-ST-02`).
- No secrets hard-coded - APNs/FCM credentials live server-side or in secure storage (`NFR-SC-01`).

## Shared concerns
- **Permission** (`FR-PN-05`): request with a clear rationale; if denied, degrade gracefully - disable
  the affected features and show an explanatory state, never block the app.
- **Deep linking** (`FR-PN-06`): a notification tap routes to the relevant screen (event detail, prayer
  times, announcement). Centralize a single deep-link router used by both local and remote taps.
- **Categories/channels** must be registered at startup so users can manage them in OS settings too.

## Testing (`QA-04`)
- Local: a reminder fires at the configured lead time with the chosen sound in airplane mode; toggles
  and lead time persist into scheduled notifications.
- Push: each category is received when opted in and suppressed when opted out; token re-registers after
  change; tap navigates to the correct screen.
