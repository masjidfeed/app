---
name: ios-simulator-debugging
description: "Workflow for debugging Masjid App issues on the iOS simulator. USE WHEN investigating iOS-simulator-only behavior, MAUI iOS tap/navigation bugs, CarouselView Position/TwoWay binding write-backs, taps landing on the wrong tab, animated scroll interruption, simctl diagnostics, screenshots, app-container preferences, console/log capture for Console.WriteLine, missing touch-injection tooling, or pointing the simulator app at the local mock. Trigger phrases include iOS simulator, simctl, iPhone 17, Home tab strip, taps wrong tab, adjacent tab, CarouselView, Position binding, IsScrollAnimated, SelectedCategoryIndex, cliclick, accessibility permission, idb, Xcode too outdated, registry_list, localhost:8090, com.goodsoftware.masjidapp."
---

# iOS Simulator Debugging - Masjid App

Use this workflow when a bug reproduces on the iOS simulator (or is reported iOS-only). The goal is to distinguish quickly between:

1. The ViewModel logic is wrong (index computed/stored incorrectly).
2. The binding path rewrote the state (native-to-VM write-back).
3. A real touch gesture is required to reproduce (timing interplay), so programmatic command execution is not equivalent.

## Hard Rules

- A programmatic `Command.Execute(...)` is **not** equivalent to a user tap for iOS scroll/gesture bugs. The Home tab-strip bug (tap About, land on Events) reproduced only through real user taps; a 6-second-later programmatic auto-tap landed correctly. Never conclude "cannot reproduce" from command execution alone - get a human to tap and capture logs during the manual session.
- There is **no synthetic touch injection** in this environment. `cliclick` and `osascript` System Events both require the Accessibility permission (not granted, `-1719`); `xcrun simctl` cannot tap; `brew install idb-companion` (facebook/fb tap) is blocked because the formula requires a newer Xcode than the repo pins (26.5). For gesture-dependent repro, ask the user to tap manually while logs are being captured.
- Temporary `Console.WriteLine` diagnostics must be removed before finishing. Stage them under a distinctive prefix (for example `DBG-TAB`) so a final `grep -r "DBG-TAB" src` proves cleanup.
- When editing the app's saved state, terminate the app first; plist edits while running are lost on relaunch.
- Keep all scratch artifacts (logs, screenshots, config backups) under `$TMPDIR/opencode`, not the repo.

## Setup Checks

Known values for this workspace:

```text
UDID:    80A8D4F0-2036-4960-9ABB-579ABB05004B   (iPhone 17)
APP ID:  com.goodsoftware.masjidapp
BUNDLE:  src/MasjidApp/bin/Debug/net10.0-ios/iossimulator-arm64/MasjidApp.app
```

```zsh
xcrun simctl list devices booted
xcrun simctl boot 80A8D4F0-2036-4960-9ABB-579ABB05004B 2>/dev/null || true
open -a Simulator
```

## Build And Deploy

`scripts/start-ios.sh` boots the simulator, builds, installs, and launches. For the debug loop (rebuild without the widget extension):

```zsh
dotnet build src/MasjidApp/MasjidApp.csproj -c Debug -f net10.0-ios --no-restore \
  -p:RuntimeIdentifier=iossimulator-arm64
xcrun simctl terminate 80A8D4F0-2036-4960-9ABB-579ABB05004B com.goodsoftware.masjidapp 2>/dev/null
xcrun simctl install 80A8D4F0-2036-4960-9ABB-579ABB05004B \
  src/MasjidApp/bin/Debug/net10.0-ios/iossimulator-arm64/MasjidApp.app
xcrun simctl launch 80A8D4F0-2036-4960-9ABB-579ABB05004B com.goodsoftware.masjidapp
```

The app restores the last visited page on relaunch (observed: relaunched straight onto Parking). Screenshot before assuming you are on Home.

## Screenshot Workflow

```zsh
xcrun simctl io 80A8D4F0-2036-4960-9ABB-579ABB05004B screenshot /tmp/masjid-screen.png
```

Screenshots capture the device display, not the macOS window - coordinates are not needed and there is no bezel math.

## Console And Log Capture

Two working paths for `Console.WriteLine` diagnostics:

1. **Attach stdout at launch** (captures everything, dies with the terminal):

   ```zsh
   xcrun simctl launch --console 80A8D4F0-2036-4960-9ABB-579ABB05004B com.goodsoftware.masjidapp \
     > /tmp/masjid-run.log 2>&1 &
   grep DBG-TAB /tmp/masjid-run.log
   ```

   Do not use `--console-pty` for a detached launch; it exits immediately when the pty closes.

2. **No attach, read the unified log afterward** (Console.WriteLine reaches os_log on the simulator):

   ```zsh
   xcrun simctl spawn 80A8D4F0-2036-4960-9ABB-579ABB05004B log show --last 15s \
     --predicate 'process == "MasjidApp"' --style compact | grep DBG-TAB
   ```

   This is the path to use while the user taps manually.

## Inspect App Saved State

The masjid registry (active masjid + config URLs) lives in the app container preferences:

```zsh
APP_DATA=$(xcrun simctl get_app_container 80A8D4F0-2036-4960-9ABB-579ABB05004B \
  com.goodsoftware.masjidapp data)
plutil -p "$APP_DATA/Library/Preferences/com.goodsoftware.masjidapp.plist"
```

Key: `masjidapp_registry_list` (JSON array of `{id, name, configUrl}`) and `masjidapp_registry_active_id`. To point the app at the local mock, terminate the app and rewrite it:

```zsh
plutil -replace masjidapp_registry_list \
  -string '[{"id":"masjid-312fc341","name":"Mock Masjid","configUrl":"http://localhost:8090/config"}]' \
  "$APP_DATA/Library/Preferences/com.goodsoftware.masjidapp.plist"
```

`MasjidConfigUrl.IsAllowed` accepts plain HTTP only for `localhost`, `127.0.0.1`, and `10.0.2.2`; anything else must be HTTPS. Start the mock with `docker compose up -d` in `devbox/` (mock API on `:8090`; the WordPress route on `:8000` needs the plugin activated or it returns 404 `rest_no_route`).

## CarouselView Position Write-Back (iOS-Only Tab Bug Family)

Symptom: tapping a category in the Home strip selects the **adjacent** tab instead of the tapped one (tap About from Announcements, land on Events). Android unaffected.

Root cause chain (MAUI 10.0.20, `CarouselViewController` on iOS):

- `Position="{Binding SelectedCategoryIndex, Mode=TwoWay}"` turns every native scroll callback into a VM write.
- The tap starts an **animated** `ScrollToItem`; an animated programmatic scroll can be interrupted (rapid re-taps while one is in flight, the `CategoryPages` Clear/Add rebuild in `ApplyFeatureFlags` on the first config load, or lazy About-content relayout mid-scroll).
- The interrupted animation's final `Scrolled` callback reports an intermediate `CenterItemIndex`; `SetPosition` writes it back through the binding and the VM follows the carousel to the neighboring index.
- The handler's `_gotoPosition` bookkeeping makes it sticky: `ScrollToPosition` skips any new goto while one is "in flight", and `SetPosition` only clears it when the center item equals the target - which never happens if the animation was cancelled. Later taps can then be silently dropped (`carousel.IsDragging` check) or skipped (`_gotoPosition != -1`), leaving VM and visual state diverged.

Diagnostic signature in logs (temporary `Console.WriteLine` in `SelectCategory` and `OnSelectedCategoryIndexChanged`):

```text
SelectCategory(index=2) current=0     <- tap About
SelectedCategoryIndex -> 2            <- VM = About
SelectedCategoryIndex -> 1            <- +150ms: native write-back -> Events (the bug)
```

A write-back line arriving **after** the command's own line, with no user action in between, is the fingerprint of this bug family.

Fix used in this repo: `IsScrollAnimated="False"` on the Home `CarouselView` (`src/MasjidApp/Views/HomeShellPage.xaml`). Programmatic jumps become instant and exact, so there is no in-flight animation to interrupt and no intermediate position events to write back; swipe navigation stays natively animated. The XAML comment there explains why it must stay false.

When source-diving the handler, fetch the exact shipped version, not `main`:

```zsh
curl -s https://raw.githubusercontent.com/dotnet/maui/10.0.20/src/Controls/src/Core/Handlers/Items/iOS/CarouselViewController.cs
```

## Transient XAML Build Failure

An incremental build can fail with `MAUIG1001: 'controls' is an undeclared prefix` for a file that is correct on disk. Retry the build; if it persists, rebuild fully. Do not "fix" the XAML based on that error alone - read the reported file first.

## Validation Checklist

- `dotnet build src/MasjidApp/MasjidApp.csproj -c Debug -f net10.0-ios --no-restore -p:RuntimeIdentifier=iossimulator-arm64` succeeds.
- Reinstall and relaunch; screenshot to confirm the expected screen (state restoration may open a pushed page).
- For interaction fixes: capture logs via `log show` while real taps are performed, confirm no late `SelectedCategoryIndex ->` write-back follows the command's own line.
- `grep -rn "DBG-" src/MasjidApp` returns nothing (temporary diagnostics removed).
- Restore any modified environment state: `mock/config.json` (keep a backup in `$TMPDIR`), the simulator `masjidapp_registry_list`, and any boot state you changed.
