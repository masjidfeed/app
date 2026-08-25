## Masjid App

### High Level Requirements
* Mobile app integrated with the Masjid API (WordPress plugin) to provide features such as prayer times, event notifications, and community engagement tools.
* Prayer times will be loaded via a SalahAPI 1.1 compatible endpoint.
* Events will be loaded via a custom endpoint provided by the Masjid API, allowing users to stay informed about upcoming activities and gatherings at the masjid.
* The app will be available for iOS, Android, and macOS (via Mac Catalyst) platforms, ensuring accessibility for a wide range of users.
* The app will use offline local notifications for prayer and iqama reminders.
* The app will include native iOS and Android widgets for displaying prayer times and upcoming events directly on the user's home screen. Mac Catalyst uses the same iOS WidgetKit widgets.
* Development will be done using MAUI, allowing for a single codebase to be used across both platforms, ensuring consistency and reducing development time.
* Local development will be conducted using emulators - WordPress running in Docker containers and Android Studio/iOS Simulator for the mobile app. 
* Coding standards and best practices will be followed to ensure maintainability and scalability of the codebase, including proper documentation and testing procedures.

---

## Detailed Requirements

### 1. Scope & Definitions

#### 1.1 Glossary
* **Masjid API** - The WordPress plugin that exposes REST endpoints consumed by the app (configuration, SalahAPI prayer-times document, events, announcements, and post descriptions).
* **SalahAPI** - The prayer-times interchange specification (v1.1) used to describe athan/iqama times, calculation methods, and Jumuah rules.
* **Athan** - The call to prayer (the calculated/observed prayer time).
* **Iqama** - The congregational prayer start time (typically offset from Athan).
* **Jumuah** - Friday congregational prayer; a masjid may run multiple Jumuah sessions.
* **Widget** - A native home-screen component (iOS WidgetKit / Android App Widget) showing prayer or event data outside the app.

#### 1.2 Requirement Notation
The key words **MUST**, **SHOULD**, and **MAY** are interpreted per RFC 2119. Each requirement has a stable ID (e.g., `FR-PT-01`) for traceability.

#### 1.3 In Scope (Future Iterations)
* **Apple Watch app** - A companion watchOS app and complications for prayer times and next-prayer countdown, fed by shared app data. Targeted for a future iteration (see Section 15).

#### 1.4 Out of Scope (v1)
* Two-way messaging or chat between users.
* User-generated content moderation.
* Windows/web clients.

---

### 2. Platforms & Technical Constraints

| ID | Requirement |
|----|-------------|
| `TC-01` | The app **MUST** be built with .NET MAUI from a single shared codebase targeting iOS, Android, and macOS (Mac Catalyst). |
| `TC-02` | Minimum supported OS versions: iOS 15.0+, Android 8.0 (API 26)+, and macOS 12.0+ (Mac Catalyst 15.0+). |
| `TC-03` | The app **MUST** support portrait orientation on phones and tablets, with responsive layouts; landscape is not required given the content type. |
| `TC-04` | Native widgets **MUST** be implemented per-platform (iOS WidgetKit in Swift; Android App Widget/Glance) and fed by shared app data. |
| `TC-05` | The app **MUST** function in English (v1). Localization infrastructure (RTL-aware resource files) **SHOULD** be established so that Arabic and additional languages can be added in v2 without structural changes. |
| `TC-06` | All network communication **MUST** use HTTPS/TLS 1.2+. |
| `TC-07` | The app **MUST** target the latest stable .NET LTS supported by MAUI at the time of release. |
| `TC-08` | The Mac Catalyst build **MUST** run on macOS 12.0 (Monterey) or later. Mac Catalyst features not present on iOS (e.g., menu bar, pointer events) **MAY** be added in a future iteration. |

---

### 3. Backend Integration (Masjid API)

| ID | Requirement |
|----|-------------|
| `API-01` | The app **MUST** retrieve prayer times from a SalahAPI 1.0-compliant endpoint exposed by the Masjid API. |
| `API-02` | The app **MUST** parse the SalahAPI JSON document including `salahapi`, `info`, `location`, `calculationMethod`, and `dailyPrayerTimes` objects. |
| `API-03` | The app **MUST** support both SalahAPI delivery modes: (a) dynamic calculation via `calculationMethod`, and (b) CSV-backed daily times via `dailyPrayerTimes.csvUrl` with `csvUrlParameters` (`fromDate`/`toDate`, static params). |
| `API-04` | When both `calculationMethod` and `dailyPrayerTimes` are present, CSV values **MUST** override calculated values for the dates they cover. |
| `API-05` | The app **MUST** retrieve events from a custom Masjid API endpoint that returns a JSON array; each event object **MUST** include: `title`, `description`, `image` (URL, optional), `eventDateTime` (ISO 8601), `location` (string, optional), `category` (string, optional), `recurrenceRule` (iCal RRULE string, optional), `alert` (string, optional), and `alertEndDateTime` (ISO 8601, optional). |
| `API-05b` | The app **MUST** retrieve announcements from a separate Masjid API endpoint; each announcement object **MUST** include: `title`, `content` (HTML/rich text), `publishedAt` (ISO 8601), and `image` (URL, optional). |
| `API-06` | The app **MUST** retrieve a configuration/bootstrap document from the Masjid API providing: masjid name, branding, endpoint URLs, contact info, and feature flags. |
| `API-07` | The app **MUST** send the SalahAPI document version and gracefully handle unknown/optional fields (forward compatibility). |
| `API-08` | All API responses **MUST** be cached locally with an ETag/Last-Modified or timestamp strategy to minimize network use and support offline access. On startup, valid cached content **MUST** render before a background revalidation; network access may block only when no usable cache exists or the user explicitly refreshes. |
| `API-09` | The app **MUST** handle API errors (timeouts, 4xx, 5xx, malformed payloads) without crashing and surface a non-blocking error/retry state to the user. |
| `API-10` | The app **MUST** support multiple masjids (selectable), with one active masjid at a time; a masjid-selection screen **MUST** be persisted across launches and accessible from Settings. |
| `API-11` | API client **MUST** enforce request timeouts and retry with exponential backoff for transient failures. |

---

### 4. Functional Requirements - Prayer Times

| ID | Requirement |
|----|-------------|
| `FR-PT-01` | The app **MUST** display the five daily prayers (Fajr, Dhuhr, Asr, Maghrib, Isha) plus Sunrise, each showing Athan and Iqama times where available. |
| `FR-PT-02` | The app **MUST** highlight the current/next prayer and show a live countdown to the next Athan and/or Iqama. |
| `FR-PT-03` | The app **MUST** display Jumuah sessions per `jumuahRules`, including name, time, and location (name + address) for each session. |
| `FR-PT-04` | The app **MUST** present a monthly/range prayer-times calendar view derived from CSV data or calculation. |
| `FR-PT-05` | Times **MUST** be rendered using the document's `timeFormat`/`dateFormat`, with a user toggle for 12h/24h display. |
| `FR-PT-06` | The app **MUST** apply the `location.timezone` (IANA) for all time computations, independent of the device timezone, and clearly indicate the masjid timezone when it differs from the device. |
| `FR-PT-07` | When using dynamic calculation, the app **MUST** apply `calculationMethod` parameters (`fajrAngle`, `ishaAngle`, `asrCalculationMethod`, `highLatitudeAdjustment`) and `iqamaCalculationRules` (including `roundMinutes`, `afterAthanMinutes`, `earliest`/`latest`, `beforeEndMinutes`, `static`). |
| `FR-PT-08` | The app **MUST** honor `overrides` for `daylightSavingsTime` and `ramadan` conditions when computing iqama/jumuah times. |
| `FR-PT-09` | The app **MUST** cache at least the current month of prayer times for offline display. |
| `FR-PT-10` | The app **MUST** allow the user to enable per-prayer Athan/Iqama reminders with configurable lead time and sound (athan tone, beep, or silent/vibrate). |

---

### 5. Functional Requirements - Events

| ID | Requirement |
|----|-------------|
| `FR-EV-01` | The app **MUST** display a list of upcoming events with title, date/time, location, description, and any active time-limited alert update. |
| `FR-EV-02` | The app **MUST** provide an event detail view supporting rich text, images, and an optional external link. |
| `FR-EV-03` | The app **MUST** allow users to add an event to their device calendar. |
| `FR-EV-04` | The app **SHOULD** support filtering/categorizing events (e.g., classes, fundraisers, community) and searching events. |
| `FR-EV-05` | The app **SHOULD** allow users to opt into reminders for specific events. |
| `FR-EV-06` | Events **MUST** be cached for offline viewing of previously loaded data. |
| `FR-EV-07` | The app **MUST** handle recurring events using an iCal RRULE string supplied in the `recurrenceRule` field; the app **MUST** expand recurrences for display and calendar export. |

---

### 6. Functional Requirements - Announcements & Community Engagement

| ID | Requirement |
|----|-------------|
| `FR-CM-01` | The app **MUST** display a feed of masjid announcements/news with timestamps. |
| `FR-CM-02` | The app **MUST** display masjid contact details (address, phone, email, website) with tap-to-call, tap-to-email, and map/directions actions. |
| `FR-CM-03` | The app **MUST** display a Qibla direction compass using device sensors and the device's current location; it **MUST** request location permission with a clear rationale and degrade gracefully if denied. |
| `FR-CM-04` | The app **MAY** include supplementary content (e.g., social links) sourced from the configuration document. |

---

### 6a. Functional Requirements - Donations

| ID | Requirement |
|----|-------------|
| `FR-DN-01` | The app **MUST** allow users to donate to the masjid by opening a donation page whose URL is provided by the Masjid API configuration document. |
| `FR-DN-02` | The donation page **MUST** open in an in-app browser (or the system browser) and **MUST** use HTTPS. |
| `FR-DN-03` | The app **MUST** hide the donation entry point when no donation URL is configured. |
| `FR-DN-04` | The app **MUST NOT** process or store any payment/card details in the client; payment handling is the responsibility of the linked donation page. |
| `FR-DN-05` | (Future) In-app donations via Stripe (one-time/recurring, Apple Pay/Google Pay, server-side PaymentIntents) **MAY** be added in a later iteration; see Section 14. |

---

### 7. Notifications

| ID | Requirement |
|----|-------------|
| `FR-PN-01` | (Future) The app **MAY** integrate remote push notifications via APNs and FCM after the Masjid API documents a delivery and token-registration contract. |
| `FR-PN-02` | (Future) Device-token registration **MAY** be added only with a documented Masjid API endpoint. |
| `FR-PN-03` | (Future) Remote notification categories **MAY** cover prayer changes, events, and announcements. |
| `FR-PN-04` | (Future) Remote category opt-in/out controls **MAY** be added with remote push. |
| `FR-PN-05` | The app **MUST** request local-notification permission with clear rationale and degrade gracefully if denied. |
| `FR-PN-06` | (Future) Remote notification taps **MAY** deep-link to relevant in-app screens. |
| `FR-PN-07` | Local notifications **MUST** be used for prayer/iqama reminders so they fire without network connectivity. |

---

### 8. Home-Screen Widgets

| ID | Requirement |
|----|-------------|
| `FR-WG-01` | The app **MUST** provide a prayer-times widget showing the next prayer name, Athan/Iqama time, and countdown. |
| `FR-WG-02` | The app **SHOULD** provide an "all today's prayers" widget variant (medium/large sizes). |
| `FR-WG-03` | The app **SHOULD** provide a way to list notifications and upcoming-events in both prayer times widgets. |
| `FR-WG-04` | Widgets **MUST** refresh on a reasonable schedule and update at prayer transitions, respecting OS background/refresh budgets. |
| `FR-WG-05` | Widgets **MUST** read from shared cached data so they render the last-known data offline. |
| `FR-WG-06` | Widgets **MUST** support light and dark appearance and the OS-configured locale. |

---

### 9. Settings & Personalization

| ID | Requirement |
|----|-------------|
| `FR-ST-01` | Users **MUST** be able to select/switch the active masjid (if multiple are configured). |
| `FR-ST-02` | Users **MUST** be able to configure notification preferences per category. |
| `FR-ST-03` | Users **MUST** be able to choose 12h/24h time format and light/dark/system theme. |
| `FR-ST-04` | Users **SHOULD** be able to select the app language where localization is available. |
| `FR-ST-05` | The app **MUST** provide an About/Version screen and links to privacy policy and support. |
| `FR-ST-06` | Users **MUST** be able to add a masjid by scanning a QR code that links to the WordPress website running the Masjid API; the app **MUST** resolve the Masjid API endpoints from the scanned URL and validate it before adding. |
| `FR-ST-08` | The app **MUST** show a plain white native launch screen during platform launch. Dynamic per-masjid branding **MUST** be applied after launch from the active masjid configuration document. |

---

### 10. Offline & Caching

| ID | Requirement |
|----|-------------|
| `NFR-OF-01` | The app **MUST** display the last-fetched prayer times, events, and announcements when offline. |
| `NFR-OF-02` | The app **MUST** show a clear "last updated" timestamp and a stale-data indicator when content is older than a configured threshold. |
| `NFR-OF-03` | Cached data **MUST** persist across app restarts using a local store (e.g., SQLite/secure preferences). |

---

### 11. Non-Functional Requirements

| ID | Requirement |
|----|-------------|
| `NFR-PF-01` | Cold start to interactive **SHOULD** be under 3 seconds on supported mid-range devices. |
| `NFR-PF-02` | Prayer time calculations/lookups **MUST** complete within 100 ms for a given day. |
| `NFR-RL-01` | The app **MUST** not crash on malformed/partial API data; failures degrade to cached or empty states. |
| `NFR-SC-01` | No secrets/API keys **MUST** be hard-coded; sensitive values **MUST** be stored in platform secure storage. |
| `NFR-SC-02` | The app **MUST** validate and sanitize all remote content rendered in the UI (no script/HTML injection). |
| `NFR-SC-03` | Donations link out to a masjid-provided page; the app **MUST NOT** store or process payment/card data. If in-app Stripe donations are added later, processing **MUST** be PCI-DSS compliant and only the Stripe publishable key may ship in the client. |
| `NFR-PR-01` | The app **MUST** minimize collected personal data; location is used only for Qibla/prayer features and **MUST** require explicit permission. |
| `NFR-PR-02` | The app **MUST** include a privacy policy and comply with App Store and Google Play data-disclosure requirements. |
| `NFR-AC-01` | The app **MUST** meet baseline accessibility: dynamic font scaling, screen-reader labels (VoiceOver/TalkBack), and sufficient color contrast. |
| `NFR-OB-01` | The app **SHOULD** integrate crash reporting and anonymous usage analytics with user consent. |

---

### 12. Local Development & DevOps

| ID | Requirement |
|----|-------------|
| `DV-01` | WordPress + the Masjid API plugin **MUST** run locally via Docker Compose for development and testing. |
| `DV-02` | The app **MUST** be runnable on the Android emulator (Android Studio), iOS Simulator, and on macOS via the Mac Catalyst target. |
| `DV-03` | The app **MUST** support switching between local/staging/prod backend instances without code changes. The active backend is determined by the selected masjid's stored `ConfigUrl`; per-masjid config is fetched from that URL at runtime. |
| `DV-04` | The local environment **SHOULD** include mock server, seed/sample data (SalahAPI document, sample events, announcements). |
| `DV-05` | CI **SHOULD** build both platforms and run automated tests on each pull request. |

---

### 13. Quality, Testing & Standards

| ID | Requirement |
|----|-------------|
| `QA-01` | The codebase **MUST** follow consistent coding standards (analyzers/linters, formatting) enforced in CI. |
| `QA-02` | Core logic (SalahAPI parsing, prayer/iqama calculation, timezone/DST handling) **MUST** have unit tests. |
| `QA-03` | API integration **SHOULD** have tests against mocked/contract responses. |
| `QA-04` | The app **SHOULD** include UI/automation tests for critical flows (view prayer times, view events, notifications opt-in). |
| `QA-05` | Public APIs and non-trivial modules **MUST** be documented; the repo **MUST** include developer setup docs. |
| `QA-06` | Prayer-time calculations **MUST** be validated against known reference values per calculation method. |

---

### 14. Future Iterations

#### 14.1 Apple Watch Companion App
Planned for a future iteration; not required for v1.

**Architecture decision:** No major cross-platform framework (MAUI, Flutter, React Native) supports running cross-platform code directly on watchOS - Apple's restricted runtime and UIKit-free environment make this a platform constraint, not a framework gap. The standard, App Store–approved pattern used by all cross-platform apps with Apple Watch support is:

1. **Native Swift/SwiftUI watchOS extension** - a lightweight Xcode target embedded in the same iOS app bundle, submitted together to the App Store.
2. **Shared data via App Group container** - a shared `NSUserDefaults` suite or SQLite database accessible by both the MAUI iOS app and the watchOS extension (identical pattern to the MAUI iOS home-screen widgets already required by `TC-04`).
3. **WatchConnectivity** - for real-time messaging between the iPhone and watch (e.g., push updated prayer times when the user opens the watch app).
4. **WidgetKit complications** - for always-visible watch face data (next prayer name/time countdown).

| ID | Requirement |
|----|-------------|
| `FR-AW-01` | A native Swift/SwiftUI watchOS extension **SHOULD** display today's prayer times and the next prayer with a live countdown. |
| `FR-AW-02` | The watchOS extension **SHOULD** provide WidgetKit complications showing the next prayer name and time on the watch face. |
| `FR-AW-03` | The watch extension **SHOULD** read prayer time data from a shared App Group container populated by the MAUI iOS app, so it renders last-known data offline. |
| `FR-AW-04` | The watch extension **SHOULD** use WatchConnectivity to receive real-time updates from the paired iPhone app. |
| `FR-AW-05` | The watch extension **SHOULD** deliver prayer/iqama reminders as local notifications on the watch. |
| `FR-AW-06` | The watch extension **MAY** surface the next upcoming event. |
| `FR-AW-07` | The watchOS extension **MUST** be embedded in the iOS app bundle and submitted as a single App Store listing; no separate watch-only app listing is required. |

#### 14.2 In-App Donations (Stripe)
Planned for a future iteration; v1 uses a masjid-provided donation link (see Section 6a).

| ID | Requirement |
|----|-------------|
| `FR-DN-F1` | The app **SHOULD** support one-time and recurring donations in-app using Stripe (e.g., PaymentSheet, Stripe subscriptions). |
| `FR-DN-F2` | The app **SHOULD** allow preset or custom amounts and a donation category/fund (e.g., general, zakat, sadaqah, building) from the configuration document. |
| `FR-DN-F3` | The Masjid API **MUST** create Stripe PaymentIntents/SetupIntents server-side; the app **MUST NOT** embed Stripe secret keys (only the publishable key). |
| `FR-DN-F4` | The app **SHOULD** support Apple Pay (iOS) and Google Pay (Android) through Stripe where available. |
| `FR-DN-F5` | Payments **MUST** be PCI-DSS compliant, idempotent (no double charges), and provide a success/failure confirmation and receipt reference. |

---

### 15. Resolved Decisions

All open questions from initial requirements analysis are now resolved and reflected in the requirements above.

| Decision | Resolution | Requirements affected |
|---|---|---|
| **Authentication** | All documented Masjid API endpoints are public, unauthenticated, and read-only. Stripe/donations server-side calls (future) will require authentication at that time. | `API-05`–`API-06` |
| **Multi-masjid** | v1 supports multiple masjids with a selection screen; one active masjid at a time. Single app listing. | `API-10` (upgraded to MUST), `FR-ST-01`, `FR-ST-06` |
| **Event schema** | WordPress post metadata extended with `eventDateTime` (ISO 8601), `location`, `category`, and `recurrenceRule` (iCal RRULE). Custom REST endpoint returns a JSON array. | `API-05`, `FR-EV-07` |
| **Branding** | Multi-tenant single app listing; dynamic branding (logo, name, theme) served per active masjid from the configuration document after the white native launch screen. | `API-06`, `FR-ST-01`, `FR-ST-08` |
| **Qibla & Athan reminders** | Both confirmed in scope for v1. | `FR-CM-03` (upgraded to MUST), `FR-PT-10` (upgraded to MUST) |
| **Localization** | English only for v1. Localization infrastructure (RTL-aware resource files) established in v1 to enable Arabic and other languages in v2 without structural rework. | `TC-05` |
| **Apple Watch** | Native Swift/SwiftUI watchOS extension embedded in the iOS bundle; data shared via App Group container and WatchConnectivity. Targeted for a future iteration. | Section 14.1 |
| **Dynamic app icon** | Not feasible - iOS and Android only permit icons bundled at build time. Requirement removed. | `FR-ST-07` removed |