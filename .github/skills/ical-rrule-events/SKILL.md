---
name: ical-rrule-events
description: Domain knowledge for modeling Masjid App events, expanding iCal RRULE recurrence rules, and exporting events to the device calendar. USE WHEN working on Home shell event summaries, event detail, recurring events, RRULE expansion, "add to calendar", or event reminders (batches B1, B2). Trigger phrases include RRULE, recurrenceRule, recurring event, iCal, FREQ, BYDAY, UNTIL, COUNT, expand occurrences, add to calendar, event reminder, eventDateTime.
---

# Events & iCal RRULE Recurrence

Requirements: `API-05`, `FR-EV-01`–`FR-EV-07`, `NFR-RL-01`, `QA-02`. Sample data:
[mock/events.json](../../../mock/events.json).

## Event model (`API-05`)
Each event object: `title`, `description` (rich text/HTML), `image` (URL, optional),
`eventDateTime` (ISO 8601), `location` (string, optional), `category` (string, optional),
`recurrenceRule` (iCal RRULE string, optional). Parse tolerantly - optional fields may be absent;
skip malformed entries without failing the whole array (`NFR-RL-01`).

## RRULE expansion (`FR-EV-07`) - the core logic
- Keep expansion **pure and unit-tested** in `MasjidApp.Core` (no MAUI deps).
- **Use a vetted iCal library** (e.g. `Ical.Net`) rather than hand-parsing RRULE - recurrence math
  (FREQ, INTERVAL, BYDAY/BYMONTHDAY/BYSETPOS, COUNT, UNTIL, leap days, month-end) is error-prone.
- `eventDateTime` is the series start (DTSTART). Anchor the RRULE to it and the event's timezone.
- **Expand over a bounded window** (e.g. now → +N months) - never enumerate an unbounded `COUNT`-less,
  `UNTIL`-less rule. Cap results.
- Produce concrete occurrences used for **both** Home shell summaries and calendar export, so display and
  export never disagree.

## Display & query (`FR-EV-01`, `FR-EV-04`)
- Home shell event summaries sorted by next occurrence: title, date/time, location, optional image.
- Detail view: sanitized rich text, image(s), optional external link (open HTTPS in browser).
- **Sanitize all remote HTML before rendering** (no script/HTML injection, `NFR-SC-02`) - share one
  trusted renderer with announcements (batch B3).

## Add to calendar (`FR-EV-03`)
- Use the platform calendar API to add a selected **occurrence** (expanded), with correct
  start/end/title/location. For a series, add the specific occurrence the user picked.

## Reminders (`FR-EV-05`, SHOULD)
- Per-event opt-in schedules a **local notification** - reuse the scheduling service from the
  notifications skill (batch C1); do not build a parallel scheduler.

## Offline (`FR-EV-06`, `NFR-OF-01`)
- Cache the fetched events array (via the resilient-api-caching seam) and render last-known events
  offline with a last-updated indicator.

## Testing (`QA-02`)
- Cover: daily/weekly/monthly rules; `COUNT` and `UNTIL` bounds; `BYDAY` sets; window capping;
  timezone correctness; tolerant parse of partial/malformed events. Name tests
  `Method_State_ExpectedBehavior`.
