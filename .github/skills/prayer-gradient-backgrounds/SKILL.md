---
name: prayer-gradient-backgrounds
description: "Use when modifying Masjid App prayer-time gradient backgrounds, prayer sky colors, countdown card gradients, or widget background gradients. Lists every MAUI, iOS WidgetKit, and Android App Widget source location that must stay in sync. Trigger phrases include prayer gradient, sky gradient, countdown background, widget_background_fajr, Fajr/Sunrise/Dhuhr/Asr/Maghrib/Isha colors."
argument-hint: "Describe the prayer gradient color change or audit needed"
---

# Prayer Gradient Backgrounds

Use this skill whenever changing the prayer-related sky gradients used by countdown cards or native widgets. Keep all platform definitions in sync unless the user explicitly asks for platform-specific styling.

## Source Locations

Update these files together:

- `src/MasjidApp/Converters/ValueConverters.cs`
  - `PrayerSkyGradientConverter` defines the MAUI app countdown-card gradients.
  - Values are RGB hex triplets passed to `CreateVerticalGradient(...)`.
  - Used by `HomeShellPage.xaml` and `PrayerTimesPage.xaml` through `PrayerSkyGradientConverter`.
- `src/MasjidApp/Platforms/iOS/MasjidWidget/MasjidWidget.swift`
  - `PrayerSkyGradient.forPrayer(_:)` defines the iOS WidgetKit gradients.
  - Values are the same RGB triplets as `UInt32` values passed to `sky(top:middle:bottom:)`.
  - `sky(...)` applies `opacity: 0.9` to each stop.
- `src/MasjidApp/Platforms/Android/Resources/drawable/widget_background_fajr.xml`
- `src/MasjidApp/Platforms/Android/Resources/drawable/widget_background_sunrise.xml`
- `src/MasjidApp/Platforms/Android/Resources/drawable/widget_background_dhuhr.xml`
- `src/MasjidApp/Platforms/Android/Resources/drawable/widget_background_asr.xml`
- `src/MasjidApp/Platforms/Android/Resources/drawable/widget_background_maghrib.xml`
- `src/MasjidApp/Platforms/Android/Resources/drawable/widget_background_isha.xml`
- `src/MasjidApp/Platforms/Android/Resources/drawable/widget_background.xml`
  - Android widget drawables use ARGB hex values with `#E6` alpha prefixed to the shared RGB values.
  - `widget_background.xml` is the fallback and should match Isha unless the fallback is intentionally changed.

## Prayer Mapping Helpers

Check these when adding, renaming, or removing a prayer-specific gradient:

- `src/MasjidApp/Platforms/Android/Widget/NextPrayerWidgetProvider.cs`
  - `GetBackgroundResource(string? prayerName)` maps prayer names to Android drawable resources.
- `src/MasjidApp/Platforms/Android/Widget/PrayerTimesWidgetProvider.cs`
  - `GetBackgroundResource(string? prayerName)` should remain in parity with the next-prayer widget provider.

## Procedure

1. Decide whether the change affects all platforms or only one surface.
2. Update the RGB source values in the MAUI converter and iOS widget together.
3. Convert each Android value to ARGB by prefixing the RGB value with `E6`, unless changing widget opacity intentionally.
4. Keep the fallback Android drawable aligned with Isha unless the fallback visual is intentionally distinct.
5. If adding a new prayer/background category, update both Android provider mapping helpers.
6. Search for `PrayerSkyGradientConverter`, `PrayerSkyGradient`, `widget_background_`, and `<gradient` to confirm no source location was missed. Ignore generated `bin/` and `obj/` outputs.

## Verification

- Run `dotnet build src/MasjidApp/MasjidApp.csproj -c Debug -f net10.0-android` after Android drawable or provider changes.
- Run `dotnet build src/MasjidApp/MasjidApp.csproj -c Debug -f net10.0-ios` after iOS widget changes when a simulator/toolchain is available.
- For MAUI-only converter changes, run the relevant app build or `dotnet build MasjidApp.slnx -c Debug` when practical.
- Visually verify the countdown cards and widgets because these values are presentation-heavy and tests are unlikely to catch poor contrast.