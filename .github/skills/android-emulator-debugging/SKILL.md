---
name: android-emulator-debugging
description: "Workflow for debugging Masjid App issues on the Android emulator. USE WHEN investigating Android emulator behavior, MAUI Android crashes, taps that visually click but do not navigate, RefreshView/ScrollView gesture problems, Shell navigation failures, adb/logcat diagnostics, UI hierarchy dumps, screenshots, package/process state, or emulator deployment issues. Trigger phrases include Android emulator, adb, logcat, Full Schedule, navigation does not work, tap/click shows but no action, RefreshView, Shell.GoToAsync, XamlParseException, OperationCanceledException, package com.goodsoftware.masjidapp, net10.0-android."
---

# Android Emulator Debugging - Masjid App

Use this workflow when a bug reproduces only, or most clearly, on the Android emulator. The goal is to distinguish four common failure classes quickly:

1. The tap never reaches the command.
2. The XAML binding points at the wrong generated command property.
3. Shell navigation executes but the destination page crashes while being created or loaded.
4. The app is not actually foregrounded, deployed, or attached to the expected emulator/package.

## Hard Rules

- Use the SDK-path `adb` binary explicitly. In this workspace `adb` is often not on `PATH`:

  ```zsh
  ADB="$HOME/Library/Android/sdk/platform-tools/adb"
  ```

- Do not run broad filesystem searches such as `find / -name adb`; they are slow and noisy. If `adb` is not on `PATH`, use the SDK path above.
- Keep logcat focused. Clear logs before the action, perform exactly one tap/navigation, then read a filtered tail.
- Treat `uiautomator dump` failures carefully. A live MAUI countdown timer can prevent Android from reaching an idle state, causing `ERROR: could not get idle state`. Use screenshots and known screen coordinates when the hierarchy dump is unavailable.
- When using `[RelayCommand]`, remember CommunityToolkit strips the `Async` suffix. A method named `GoToPrayerTimesAsync` generates `GoToPrayerTimesCommand`, not `GoToPrayerTimesAsyncCommand`.

## Setup Checks

Run these first:

```zsh
ADB="$HOME/Library/Android/sdk/platform-tools/adb"
"$ADB" devices
"$ADB" shell pm list packages | grep -iE 'masjid|bothell|company'
"$ADB" shell pidof com.goodsoftware.masjidapp || true
"$ADB" shell dumpsys window | grep -E 'mCurrentFocus|mFocusedApp' | tail -5
```

Known package for the current app head:

```text
com.goodsoftware.masjidapp
```

Known activity form from Android dumpsys:

```text
com.goodsoftware.masjidapp/crc64a326ea4a2cdc8c4a.MainActivity
```

Bring the app to the foreground without random input:

```zsh
ADB="$HOME/Library/Android/sdk/platform-tools/adb"
"$ADB" shell am start -n com.goodsoftware.masjidapp/crc64a326ea4a2cdc8c4a.MainActivity >/dev/null
```

## Build And Deploy

For a focused Android validation, use:

```zsh
dotnet build src/MasjidApp/MasjidApp.csproj -c Debug -f net10.0-android
dotnet build -t:Run -f net10.0-android src/MasjidApp/MasjidApp.csproj
```

If debugging through VS Code, follow the repo memory/dev docs: let the MAUI extension inject its own `maui: Build` task; do not add a custom `preLaunchTask` to MAUI launch configs.

## Screenshot And Tap Workflow

Capture the current screen:

```zsh
ADB="$HOME/Library/Android/sdk/platform-tools/adb"
"$ADB" exec-out screencap -p > /tmp/masjid-screen.png
```

Tap a known coordinate and collect logs:

```zsh
ADB="$HOME/Library/Android/sdk/platform-tools/adb"
"$ADB" logcat -c
"$ADB" shell input tap X Y
"$ADB" exec-out screencap -p > /tmp/masjid-after.png
"$ADB" logcat -d -t 1200 \
  | grep -iE 'AndroidRuntime|FATAL EXCEPTION|mono-rt|DOTNET|MasjidApp|Exception|Unhandled|XamlParse|Shell|Navigation|OperationCanceled|Binding|Command' \
  | tail -160
```

Useful emulator facts from this workspace/session:

- Physical size observed: `1080x2400`.
- Home `Full Schedule` button was around `x=895, y=760` on that emulator.

Always recapture a screenshot before reusing coordinates; layout, density, scroll position, or pushed pages can move targets.

## UI Hierarchy Workflow

Try this when the page is idle enough:

```zsh
ADB="$HOME/Library/Android/sdk/platform-tools/adb"
"$ADB" shell uiautomator dump /sdcard/window.xml >/dev/null
"$ADB" exec-out cat /sdcard/window.xml \
  | tr '<' '\n' \
  | grep -E 'text="(Full Schedule|Today|Events|Prayer Times|Announcements|Settings)'
```

If it fails with `ERROR: could not get idle state`, do not just fall back to screenshots. The accessibility tree cannot reach idle whenever any view keeps animating - a one-second countdown `Label`, an `ActivityIndicator`, or a continuously invalidating `GraphicsView` all trigger this. Screenshots show pixels but not bounds, clickability, or overlay stacking, which is exactly what you need for a dead-tap bug.

Prefer `dumpsys activity top`, which dumps the native view tree with bounds and flags and does **not** require idle:

```zsh
ADB="$HOME/Library/Android/sdk/platform-tools/adb"
"$ADB" shell dumpsys activity top | sed -n '/View Hierarchy/,/Looper/p' | head -200
```

Each view line ends with a flag block, decoded left-to-right:

```text
[visibility V=visible I=invisible G=gone]
[F focusable] [E enabled] [D drawn] [scrollbars H/V]
[C clickable] [L long-clickable] [X context-clickable]
```

A view with `.` where `C` would be (for example `V.ED.....`) is **not** clickable and passes touches through to views beneath it; a view with `C` consumes the touch. Use this to find which view actually owns a tapped coordinate and whether a full-screen overlay is intercepting input.

Note: MAUI on Android renders a full-screen, topmost `Microsoft.Maui.Graphics.Platform.PlatformGraphicsView{0,0-WxH}`. It is normally non-clickable (`V.ED.....`) and therefore transparent to input - confirm its flags before blaming it for swallowed taps.

## Debugging Tap Shows But Navigation Does Not Happen

Check these in order:

1. **Command name:** For `[RelayCommand] private Task FooAsync()`, bind to `FooCommand`.

   Bad:

   ```xml
   Command="{Binding FooAsyncCommand}"
   ```

   Good:

   ```xml
   Command="{Binding FooCommand}"
   ```

2. **Generated command exists:** Search generated docs/obj output or build XML for `FooCommand` if needed.

   ```zsh
   rg 'FooCommand|FooAsyncCommand' src/MasjidApp/obj -g '*' --hidden
   ```

3. **Tap target:** Small `Label` + `TapGestureRecognizer` targets inside `RefreshView`/`ScrollView` can be unreliable or easy to miss on Android. Prefer controls with at least a 44px touch target.

  Also avoid nesting a `CollectionView` inside a `ScrollView`. With no bounded height the inner list measures every item unbounded and over-expands (observed ~1879px tall for 3 items in `dumpsys activity top` bounds), which can push real content off-screen and create overlapping/misaligned hit regions. For small, bounded lists use `BindableLayout` on a `VerticalStackLayout` instead.
4. **Destination crash:** If navigation executes but the app returns to launcher or stays visually unchanged, inspect logcat for `FATAL EXCEPTION`. The target page may crash in `InitializeComponent`, `OnAppearing`, or its `LoadCommand`.

5. **Prove whether the command fires at all.** Before theorizing about navigation, instrument the command body with a temporary log line and watch logcat. MAUI managed `Debug.WriteLine` output appears under the `app_process64` tag:

   ```csharp
   System.Diagnostics.Debug.WriteLine("DIAGTAP: SelectAnnouncement fired");
   ```

   ```zsh
   ADB="$HOME/Library/Android/sdk/platform-tools/adb"
   "$ADB" logcat -c; "$ADB" shell input tap X Y
   "$ADB" logcat -d | grep -E 'DIAGTAP|app_process64' | tail -40
   ```

   First validate the logging path with a control you *know* works (the category strip's `SelectCategoryCommand` fires reliably). Then you can cleanly classify the failure: **no DIAGTAP** means the tap never reached the command (gesture/overlay/binding problem); **DIAGTAP but no page change** means the command ran and navigation itself failed. Remove all DIAGTAP lines when done.

6. **Whole region is dead, not one control.** If *every* control inside a scrollable content area ignores taps while a sibling region (sticky header strip, bottom Shell tabs) still works, stop testing individual controls. Bisect the container: temporarily remove the `RefreshView`, then the `ScrollView`, then convert nested `CollectionView`s, rebuilding between each. A region-wide dead zone points at the container/overlay stack or off-main-thread state corruption (next section), not at any single binding. This class of bug was observed in this repo and is not yet fully root-caused - capture `dumpsys activity top` flags for the dead region when you hit it.

## MAUI Failure Patterns Seen In This Repo

### Wrong RelayCommand Binding

Symptom: button/tap animation appears, no navigation, no obvious logcat exception.

Cause: XAML used `GoToPrayerTimesAsyncCommand`, etc. CommunityToolkit generated `GoToPrayerTimesCommand`, etc.

Fix: remove `Async` from XAML command property names.

### Destination Load Cancellation Crash

Symptom: navigation starts, then Android logs a fatal `System.OperationCanceledException` through `AsyncRelayCommand.AwaitAndThrowIfFailed`, often from provider/API load code.

Log shape:

```text
FATAL EXCEPTION: main
android.runtime.JavaProxyThrowable: [System.OperationCanceledException]: The operation was canceled.
...
MasjidApp.ViewModels.PrayerTimesViewModel+<LoadAsync>
CommunityToolkit.Mvvm.Input.AsyncRelayCommand+<AwaitAndThrowIfFailed>
```

Fix: in UI-bound `LoadAsync(CancellationToken ct)` commands, catch `OperationCanceledException` and leave `finally { IsBusy = false; }` intact. Home and splash already use this pattern; destination pages loaded during navigation should too.

### Destination XAML Parse Crash

Symptom: Shell route executes, Android kills app during page creation.

Log shape:

```text
Microsoft.Maui.Controls.Xaml.XamlParseException: StaticResource not found for key x:Static Color.Orange
at MasjidApp.Views.SomePage.InitializeComponent
```

Fix: correct invalid XAML resource expressions. In this repo, use `TextColor="Orange"` or define/use a real color resource; do not write `StaticResource Warning, x:Static Color.Orange` inside `AppThemeBinding`.

### Off-Main-Thread Collection Mutation

Symptom: with a `CollectionView`, the list silently fails to render or interaction (taps/scroll) is corrupted with no obvious crash. After converting the same list to `BindableLayout`, the app crashes on load with:

```text
Android.Util.AndroidRuntimeException: Only the original thread that created a view hierarchy can touch its views.
Expected: main Calling: Thread-7
```

Cause: a UI-bound `ObservableCollection` (for example `RecentAnnouncements`, `UpcomingEvents`) was mutated on a background thread. This happens when a ViewModel `LoadAsync` uses `.ConfigureAwait(false)` and then assigns/clears the collection in the continuation - the continuation resumes off the main thread. `CollectionView` masks the violation (silent state corruption); `BindableLayout` surfaces it as the exception above.

Fix: per `.github/instructions/csharp.instructions.md`, omit `.ConfigureAwait(false)` in UI-bound MAUI code so continuations resume on the main thread. Only use `ConfigureAwait(false)` in the platform-agnostic `MasjidApp.Core` library, never where the continuation touches bindable UI state.

### Cleartext HTTP Images Fail Silently

Symptom: API JSON loads fine and lists populate, but remote `Image` sources never render - no exception, just blank/placeholder images.

Cause: the typed API client uses `HttpClient`/`SocketsHttpHandler`, which is **not** subject to Android's `usesCleartextTraffic` policy, so JSON over plain HTTP succeeds. MAUI's `Image` uses the native Android image pipeline, which **is** subject to that policy (cleartext disallowed by default on API 28+), so `http://` images are dropped silently. Against the local mock the emulator reaches the host at `10.0.2.2` (not `localhost`/`127.0.0.1`), over plain HTTP - so images hit exactly this asymmetry.

Fix: add a network security config that permits cleartext only for the dev hosts and reference it from the manifest. Do not globally enable cleartext.

`src/MasjidApp/Platforms/Android/Resources/xml/network_security_config.xml`:

```xml
<network-security-config>
  <base-config cleartextTrafficPermitted="false" />
  <domain-config cleartextTrafficPermitted="true">
    <domain includeSubdomains="true">10.0.2.2</domain>
    <domain includeSubdomains="true">localhost</domain>
  </domain-config>
</network-security-config>
```

`AndroidManifest.xml`:

```xml
<application android:networkSecurityConfig="@xml/network_security_config" ... />
```

## Navigation-Specific Checks

Read these files when debugging Home navigation:

- `src/MasjidApp/Views/HomeShellPage.xaml`
- `src/MasjidApp/ViewModels/HomeShellViewModel.cs`
- `src/MasjidApp/Services/Routes.cs`
- `src/MasjidApp/Services/NavigationService.cs`
- `src/MasjidApp/AppShell.xaml.cs`

Expected route shape:

- Bottom-tab routes are absolute, for example `//home`.
- Pushable feature routes are relative and registered in `AppShell.RegisterRoutes()`, for example `prayertimes`, `events`, and `announcements`.
- `NavigationService.GoToAsync` marshals to the main thread and calls `Shell.Current.GoToAsync(...)`.

## Validation Checklist

Before declaring the issue fixed:

- Build Android cleanly:

  ```zsh
  dotnet build src/MasjidApp/MasjidApp.csproj -c Debug -f net10.0-android
  ```

- Deploy/run on emulator:

  ```zsh
  dotnet build -t:Run -f net10.0-android src/MasjidApp/MasjidApp.csproj
  ```

- For each fixed interaction:
  - Clear logcat.
  - Tap exactly one control.
  - Capture a screenshot after the tap.
  - Confirm the expected page/screen is visible.
  - Confirm focused logcat has no `FATAL EXCEPTION`, `XamlParseException`, unhandled `OperationCanceledException`, or binding/command warning.

- If the bug touches core logic, run the core tests too:

  ```zsh
  dotnet test tests/MasjidApp.Core.Tests/MasjidApp.Core.Tests.csproj -c Debug
  ```

- For repo Definition of Done, also run:

  ```zsh
  dotnet build MasjidApp.slnx -c Debug
  ```
