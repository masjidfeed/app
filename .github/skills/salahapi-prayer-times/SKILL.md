---
name: salahapi-prayer-times
description: Domain knowledge for parsing SalahAPI 1.0 documents and computing prayer (athan) and iqama times for the Masjid App. USE WHEN working on prayer-time parsing, calculation, iqama rules, Jumuah, timezone/DST/Ramadan overrides, CSV-backed daily times, prayer reminders, or prayer widgets (batches A1–A4, C1, D1). Trigger phrases include SalahAPI, athan, iqama, fajr/dhuhr/asr/maghrib/isha/sunrise, calculationMethod, iqamaCalculationRules, jumuahRules, overrides, csvUrl, fajrAngle, ishaAngle, asrCalculationMethod, highLatitudeAdjustment, prayer countdown, calculation method reference values.
---

# SalahAPI 1.0 - Prayer & Iqama Times

Authoritative spec: <https://github.com/salahapi/salahapi-specification/blob/main/versions/1.0.md>.
Sample document: [mock/salahapi.json](../../../mock/salahapi.json). Requirements: `API-01`–`API-04`,
`API-07`, `FR-PT-01`–`FR-PT-10`, `NFR-PF-02`, `QA-02`, `QA-06`.

## Library
Use the [`SalahAPI`](https://www.nuget.org/packages/SalahAPI/1.1.0) NuGet package (**v1.1.0**) in
`MasjidApp.Core` for **both** SalahAPI document parsing and prayer-time calculation - do not hand-roll the
astronomy or the document parser. Add it with `dotnet add package SalahAPI --version 1.1.0`. Key surface:
- `SalahAPI.Calculations.PrayerTimes` - e.g. `new PrayerTimes(Method.MethodIsna).GetTimesForToday(latitude, longitude, timezone)`.
- `SalahAPI.Calculations.Builder` - `BuildCsv(fromDate, toDate)` for date-range CSV generation.
- `SalahAPI.Calculations.HijriDateConverter` - `ConvertToHijri(...)` / `IsRamadan(...)` for Hijri dates and Ramadan detection.

Map the masjid's SalahAPI document fields (below) onto these APIs.

## Core rules (do not violate)
- **All time math uses `location.timezone` (IANA), never the device timezone** (`FR-PT-06`). Surface the
  masjid timezone in the UI when it differs from the device.
- **Parse tolerantly**: ignore unknown/optional fields, tolerate missing sections, never throw on
  malformed/partial payloads - degrade to cached/empty (`API-07`, `NFR-RL-01`).
- **CSV overrides calculation** for the dates it covers (`API-04`).
- Keep all of this in `MasjidApp.Core` (no MAUI/UI deps) so it is fully unit-testable.

## Document shape
| Object | Key fields |
|--------|-----------|
| `salahapi` | version string (e.g. `"1.0"`) - read and carry for forward-compat |
| `info` | `name`, `description`, `updated` |
| `location` | `name`, `address`, `latitude`, `longitude`, `elevation`, `timezone` (IANA) |
| `calculationMethod` | `name`, `fajrAngle`, `ishaAngle`, `asrCalculationMethod`, `highLatitudeAdjustment` |
| `iqamaCalculationRules` | per prayer: `afterAthanMinutes`, `roundMinutes`, `earliest`, `latest`, `beforeEndMinutes`, `static` |
| `jumuahRules` | array of `{ name, time, location:{ name, address } }` (multiple sessions) |
| `overrides` | `daylightSavingsTime` and `ramadan` - partial rule sets layered onto iqama/jumuah |
| `dailyPrayerTimes` | `csvUrl`, `csvUrlParameters` (`fromDate`, `toDate`, `static` params) |

## Athan (prayer) calculation
Compute the five daily prayers plus **Sunrise** from solar position for the date/lat/lon/elevation:
- **Fajr / Isha**: depression angles `fajrAngle` / `ishaAngle` below the horizon.
- **Dhuhr**: solar noon (+ small transit correction).
- **Asr**: shadow ratio - `Standard` (Shafi'i/Maliki/Hanbali) = shadow length 1×; `Hanafi` = 2×.
- **Maghrib**: sunset; **Sunrise** for display/iqama anchoring.
- **`highLatitudeAdjustment`**: apply the named strategy (e.g. `AngleBased`, `MidNight`, `OneSeventh`)
  when the sun does not reach the required depression angle.

> **Use the [`SalahAPI`](https://www.nuget.org/packages/SalahAPI/1.1.0) NuGet package (v1.1.0)** for the
> athan computation (`SalahAPI.Calculations.PrayerTimes`) rather than hand-deriving astronomy. Map
> `calculationMethod.name` to the package's `Method`, and pass `location.latitude`/`longitude`/`timezone`.
> The package matches common method reference values, which keeps `QA-06` validation straightforward.

## Iqama calculation
Resolve each prayer's iqama from its rule, in this order:
1. If `static` (e.g. `"13:30"`) → use that fixed local time.
2. Else `athan + afterAthanMinutes`.
3. Apply `roundMinutes` (round up/nearest to the configured increment - match the spec's rounding).
4. Clamp to `earliest`/`latest` when present.
5. `beforeEndMinutes` sets the time relative to the end of the prayer window (e.g. before the next
   prayer's athan) - use for rules expressed that way.

**Jumuah**: produce one entry per `jumuahRules` item with name, time, and location (name + address).

## Overrides (`FR-PT-08`)
Layer `overrides.daylightSavingsTime` when the computed date is in DST for the masjid timezone, and
`overrides.ramadan` during Ramadan (use `HijriDateConverter.IsRamadan(...)` from the SalahAPI package to
detect Ramadan). Overrides are **partial** - merge them over the base
`iqamaCalculationRules`/jumuah, replacing only the keys they specify.

## CSV-backed daily times (`API-03`, `API-04`)
- Build the URL from `csvUrl` + `csvUrlParameters` (`fromDate`/`toDate` + static params). To generate CSV
  locally (e.g. for tests/fixtures) use the package's `SalahAPI.Calculations.Builder.BuildCsv(...)`.
- Parse tolerantly into per-date athan/iqama rows; skip malformed rows without failing the import.
- **Merge:** for any date the CSV covers, CSV values win over calculation; outside the range, calculate.
- Cache ≥ the current month for offline display (`FR-PT-09`).

## Display & performance
- Render with the document `timeFormat`/`dateFormat`; honor the user's 12h/24h toggle (`FR-PT-05`).
- Highlight current/next prayer; drive a live countdown to the next athan and/or iqama (`FR-PT-02`).
- A full day's computation MUST complete < 100 ms (`NFR-PF-02`).

## Testing (`QA-02`, `QA-06`) - required
- Validate calculated athan/iqama against **known reference values per calculation method** (at minimum
  ISNA plus one other, e.g. MWL or Umm al-Qura) within method tolerance.
- Cover: a DST transition day, a Ramadan-override day, `static`/`earliest`/`latest`/`roundMinutes`
  iqama branches, CSV-vs-calc merge, and tolerant parsing of partial/unknown-field documents.
- Name tests `Method_State_ExpectedBehavior` (xUnit + FluentAssertions).
