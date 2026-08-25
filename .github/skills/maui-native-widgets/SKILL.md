---
name: maui-native-widgets
description: Domain knowledge for the Masjid App's native home-screen widgets - iOS WidgetKit (Swift) and Android App Widget/Glance - fed by a shared cache the MAUI app writes. USE WHEN working on home-screen widgets, the shared App Group / shared-data container, widget timelines/refresh, next-prayer widget, all-today's-prayers variant, or widget light/dark/locale support (batch D1). Trigger phrases include widget, WidgetKit, Glance, App Widget, App Group, TimelineProvider, shared container, widget refresh, next prayer widget.
---

# Native Home-Screen Widgets

Widgets are **native per-platform** (no MAUI cross-platform widget) and **read only from shared cached
data** - they must never hit the network directly. Requirements: `TC-04`, `FR-WG-01`–`FR-WG-06`.

## Architecture
- The MAUI app computes/caches prayer data, then **writes a small render-ready snapshot** to a shared
  store the widget process can read.
  - **iOS:** App Group container - shared `UserDefaults` suite or a file/SQLite in the group container.
    The WidgetKit extension (Swift/SwiftUI) reads it. (Same App Group pattern noted for the future
    Apple Watch extension.)
  - **Android:** a shared store readable by the App Widget / Glance provider (e.g. a file or
    DataStore/SQLite the app writes and the widget reads).
- Snapshot should be **pre-resolved** (next prayer name, athan/iqama time, countdown anchor, today's
  list) so the widget does zero domain computation - just formatting.

## Content (`FR-WG-01`–`FR-WG-03`)
- **Small:** next prayer name, athan/iqama time, countdown.
- **Medium/large (`FR-WG-02`, SHOULD):** all of today's prayers.
- Optionally surface upcoming events/notifications where size allows (`FR-WG-03`, SHOULD).

## Refresh (`FR-WG-04`)
- Update on a reasonable schedule **and at prayer transitions**, respecting OS background/refresh budgets.
  - iOS: provide a `TimelineProvider` with entries at each prayer transition so the widget advances
    without a wake; request reloads when the app refreshes data.
  - Android: schedule updates (e.g. WorkManager/AlarmManager) aligned to prayer transitions; avoid
    over-frequent updates that the OS will throttle.
- The app should refresh the shared snapshot whenever prayer data, active masjid, or day changes, then
  ask the widget to reload.

## Offline & appearance (`FR-WG-05`, `FR-WG-06`)
- Render **last-known snapshot offline** (it only reads the shared store).
- Support **light and dark** appearance and the **OS-configured locale**; use localized strings and the
  user's 12h/24h preference where exposed in the snapshot.

## Gotchas
- Keep the snapshot tiny and serialization-stable - widgets have strict memory/time limits.
- Write the snapshot atomically; widgets may read mid-update.
- Don't put secrets or full API payloads in the shared container - only display-ready fields.
