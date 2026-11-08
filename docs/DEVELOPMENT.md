# Developer Setup

Setup and workflow for the Masjid App. Requirements referenced by ID (e.g. `DV-01`) live in [REQUIREMENTS.md](REQUIREMENTS.md).

## Prerequisites

> **On a clean macOS machine, run [`scripts/bootstrap-macos.sh`](../scripts/bootstrap-macos.sh)** to
> install and configure all of the below automatically (Homebrew, the .NET SDK + MAUI workload,
> Microsoft OpenJDK 21, the Android SDK + an AVD, Docker Desktop, and the VS Code MAUI extension). The
> script is idempotent - re-run it any time. Full Xcode must be installed from the App Store separately.

- **.NET SDK** (latest stable LTS supported by MAUI, `TC-07`) with the MAUI workload:
  ```sh
  dotnet workload install maui
  ```
- **Microsoft OpenJDK 21** - required by the .NET MAUI Android toolchain:
  ```sh
  brew install --cask microsoft-openjdk@21
  ```
- **Docker Desktop** (for the local backend, `DV-01`).
- **Xcode** + iOS Simulator (macOS) and/or the **Android SDK** with an emulator (`DV-02`).

## 1. Start the local backend

The backend is WordPress + the Masjid API plugin, plus a static mock API that serves seed JSON so you can develop without configuring WordPress first (`DV-01`, `DV-04`). The Docker setup lives in [devbox/](../devbox) (custom WordPress image with wp-cli, Composer, Xdebug, and OpenTelemetry).

```sh
cd devbox
docker compose up -d
```

| Service | URL | Purpose |
|---------|-----|---------|
| WordPress | http://localhost:8000 | Masjid API plugin host |
| phpMyAdmin | http://localhost:8080 | Database UI |
| Mock API | http://localhost:8090 | Seed data for offline/contract dev |
| Mailpit | http://localhost:8025 | Captured outbound mail |
| Jaeger | http://localhost:16686 | OpenTelemetry traces |

Mock endpoints: `/salahapi`, `/events`, `/announcements`, `/config` (served from [mock/](../mock)).

To develop the WordPress plugin, place it under `backend/wp-content/plugins/masjid-api/` - the `backend/wp-content` folder is mounted into the container (and git-ignored). Xdebug listens on port 9003.

### WordPress REST API endpoints

The `masjid-api` plugin ([backend/wp-content/plugins/masjid-api/masjid-api.php](../backend/wp-content/plugins/masjid-api/masjid-api.php)) exposes these public endpoints on the WordPress service (`http://localhost:8000`):

| Method | Path | Description |
|--------|------|-------------|
| GET | `/wp-json/masjid/v1/config` | Per-masjid configuration document - same shape as `mock/config.json` with endpoint URLs rewritten to `:8000` |
| GET | `/wp-json/masjid/v1/salahapi` | SalahAPI 1.0 document - same shape as `mock/salahapi.json` |
| GET | `/wp-json/masjid/v1/events` | Events array - same shape as `mock/events.json` |
| GET | `/wp-json/masjid/v1/announcements` | Announcements array - same shape as `mock/announcements.json` |
| POST | `/wp-json/masjid/v1/push/register` | Register a push token: `{"platform":"ios"\|"android","token":"..."}` → `{"registered":true}` |

**Differences from the mock API (`mock/`):**
- `/config` - `endpoints.*` URLs point at `:8000/wp-json/masjid/v1/…` instead of `:8090/…` so all traffic goes through WordPress when using `:8000` as the base URL.
- `/salahapi` - `dailyPrayerTimes.csvUrl` points at `/wp-json/masjid/v1/prayer-times-csv` (not yet implemented; app falls back to calculation-only mode until the CSV route is added).
- Push tokens are stored in the WordPress options table under `masjid_push_tokens` (production deployments should replace this with a dedicated store or third-party push service).

To activate the plugin after `docker compose up -d`, navigate to http://localhost:8000/wp-admin → Plugins → Activate **Masjid API** (or use `wp-cli`: `wp plugin activate masjid-api`).

## 2. Configure environments

Base URLs for `local` / `staging` / `prod` are environment-configurable without code changes (`DV-03`). For local dev, point the app's base URL at the mock API (`http://localhost:8090`) or your WordPress instance (`http://localhost:8000`). Android emulators reach the hostscripts/deploy-ios-device.sh via `http://10.0.2.2:<port>` instead of `localhost`.

> Never commit real secrets. Local dev credentials are hardcoded in [devbox/docker-compose.yml](../devbox/docker-compose.yml) for convenience; runtime secrets use platform secure storage (`NFR-SC-01`).

## 3. Run the app

### Android map preview

The Home/About location preview uses the Google Maps SDK on Android. Create a Google
Maps Platform key with **Maps SDK for Android** enabled, then restrict it to the Android
application `com.goodsoftware.masjidapp` and the SHA-1 fingerprints of the signing
certificates that are allowed to build the app. Use separate restricted keys for debug and
release signing where possible.

Provide the key only at build time; never add it to `AndroidManifest.xml`, source control,
or a checked-in properties file (`NFR-SC-01`):

```sh
# Environment variable (preferred for local shells and CI secrets)
export GOOGLE_MAPS_API_KEY="<restricted-key>"
dotnet build -t:Run -f net10.0-android src/MasjidApp/MasjidApp.csproj

# Equivalent one-off MSBuild property
dotnet build -t:Run -f net10.0-android \
  -p:GoogleMapsApiKey="<restricted-key>" \
  src/MasjidApp/MasjidApp.csproj
```

`scripts/start-android.sh` pulls the key automatically: it downloads the same
`google-maps-api-key` secret from Azure Key Vault the release build uses
(`az` must be logged in), caches it at `~/.config/masjidapp/google-maps-api-key`
(mode 600), and exports it for the build. `REFRESH_MAPS_KEY=1` re-downloads the
cached value, and `PUBLISHING_KEY_VAULT_NAME`/`GOOGLE_MAPS_API_KEY`/`GOOGLE_MAPS_API_KEY_FILE`
override the vault name, key value, and cache path respectively. When the vault
is unreachable the script warns and builds keyless instead of failing.

The emulator runs debug builds signed with the Android debug certificate; add its
SHA-1 fingerprint alongside the `com.goodsoftware.masjidapp` package name to the
key's application restrictions in Google Cloud Console, or tiles stay blank even
with the key injected:

```sh
keytool -list -v -keystore ~/.android/debug.keystore -alias androiddebugkey -storepass android
```

The project injects the value into the generated Android manifest. When no key is supplied,
the app intentionally does not construct the embedded Google map and shows the branded
address card instead; tapping it still opens an installed maps app or the HTTPS fallback.
MapKit previews on iOS and Mac Catalyst require no key. The preview geocodes only the
masjid's public configured address and does not request or use the device's location.

```sh
# iOS Simulator
dotnet build -t:Run -f net10.0-ios src/MasjidApp/MasjidApp.csproj

# Android emulator
dotnet build -t:Run -f net10.0-android src/MasjidApp/MasjidApp.csproj

# macOS (Mac Catalyst - requires macOS 12.0+, TC-08)
dotnet build -t:Run -f net10.0-maccatalyst src/MasjidApp/MasjidApp.csproj
```

### Deploying to a physical iOS device

Connect an iOS device via USB (or Wi-Fi pairing), unlock it, and accept the "Trust This
Computer?" prompt, then run:

```sh
scripts/deploy-ios-device.sh
```

The script auto-detects the connected device when only one is attached (pass a UDID as the
first argument to target a specific one otherwise). Unlike simulator builds - which use ad-hoc
signing (see `MasjidApp.csproj`) - device deploys need a real Apple Development certificate and a
provisioning profile that includes the App Group capability
(`group.com.goodsoftware.masjidapp`, required by the WidgetKit extension) for
`com.goodsoftware.masjidapp`. The simplest way to provision this once
is to open the project in Xcode (the generated `.xcodeproj` under `src/MasjidApp/obj/`, or add the
app target in "Signing & Capabilities"), select your Apple ID/team, and let Xcode automatically
manage signing - the resulting profile is cached in `~/Library/MobileDevice/Provisioning
Profiles` for subsequent `dotnet build` runs.

## Branded Tenant Assets

[`branded_resources/branding.yaml`](../branded_resources/branding.yaml) is the source of truth for
native tenant branding. Each organization supplies its ID, HTTPS website, and source PNG. After
changing that file or an icon, regenerate the tenant definition, Android launcher aliases, iOS
alternate-icon entries, and MAUI/Android/iOS icon sizes on macOS:

```sh
scripts/generate-branded-resources.sh
```

The script uses the macOS-provided `sips` tool. Use `scripts/generate-branded-resources.sh --check`
in CI or before review to fail when generated files are stale.

## Observability (OpenTelemetry)

The app is instrumented with [OpenTelemetry](https://opentelemetry.io/) for traces, metrics, and logs (`NFR-OB-01`).

**Instrumentation model - layered so the domain library stays SDK-agnostic:**

- **`MasjidApp.Core`** instruments only via .NET's built-in `System.Diagnostics` primitives - an `ActivitySource`, a `Meter`, and `Microsoft.Extensions.Logging.Abstractions` `ILogger`. It does **not** reference the OpenTelemetry SDK. The shared names/instruments live in [src/MasjidApp.Core/Observability/MasjidTelemetry.cs](../src/MasjidApp.Core/Observability/MasjidTelemetry.cs) (source name `MasjidApp`).
- **`MasjidApp`** (the app head) composes the OpenTelemetry SDK and OTLP exporter in `ConfigureObservability` ([src/MasjidApp/MauiProgram.cs](../src/MasjidApp/MauiProgram.cs)) - `AddSource("MasjidApp")`, `AddMeter("MasjidApp")`, and an OpenTelemetry logging provider.

**Exception handling.** Resilience-boundary `catch` blocks (third-party library, network I/O, timezone lookup) degrade gracefully per `NFR-RL-01` but are no longer silent: each logs the exception via a source-generated `ILogger` message and increments the `masjidapp.degraded_operations` counter (tagged with the `operation` name, e.g. `prayer_time_calculation`, `dst_lookup`, `csv_url_build`, `csv_fetch`). High-level handled errors go through `TelemetryService.TrackError`, which emits a structured error log.

**Consent gating (`NFR-OB-01`).** Telemetry is only **exported off-device** when the user has opted in. Consent is stored in `Preferences` under the `telemetry_consent` key (default `false`) and toggled from Settings. Activities, metrics, and logs are always collected in-process, but the OTLP exporters are only registered when consent is granted.

**Configuring the OTLP endpoint (`DV-03`).** The exporter reads the standard `OTEL_EXPORTER_OTLP_ENDPOINT` environment variable, so no code change is needed to point it at a collector. The local devbox runs Jaeger with an OTLP receiver (see the service table above); for example:

```sh
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
```

View collected traces in Jaeger at http://localhost:16686.

## 4. Definition of Done (every change)

Run these locally before opening a PR - CI enforces the same (`QA-01`, `QA-02`):

```sh
dotnet format src/MasjidApp.Core/MasjidApp.Core.csproj --verify-no-changes  # formatting + analyzers
dotnet build  src/MasjidApp.Core/MasjidApp.Core.csproj -c Release            # warnings-as-errors
dotnet test   tests/MasjidApp.Core.Tests/MasjidApp.Core.Tests.csproj -c Release  # unit + contract tests
```

## Planned solution layout

CI and the commands above assume this structure (create it when scaffolding the app):

```
MasjidApp.slnx
src/
  MasjidApp/            # .NET MAUI app head (iOS + Android + macOS Mac Catalyst)
  MasjidApp.Core/       # platform-agnostic domain logic (no MAUI deps) - fully unit-tested
tests/
  MasjidApp.Core.Tests/ # xUnit + FluentAssertions
```

## Running CI checks locally

Run these before opening a PR - CI enforces the same gates on `ubuntu-latest` (`DV-05`, `QA-01`):

```sh
# Restore (Core library + test project only; no MAUI workload needed)
dotnet restore src/MasjidApp.Core/MasjidApp.Core.csproj
dotnet restore tests/MasjidApp.Core.Tests/MasjidApp.Core.Tests.csproj

# Format check (fails on any violation - QA-01)
dotnet format src/MasjidApp.Core/MasjidApp.Core.csproj --verify-no-changes --severity warn
dotnet format tests/MasjidApp.Core.Tests/MasjidApp.Core.Tests.csproj --verify-no-changes --severity warn

# Warnings-as-errors build (QA-01)
dotnet build src/MasjidApp.Core/MasjidApp.Core.csproj -c Release
dotnet build tests/MasjidApp.Core.Tests/MasjidApp.Core.Tests.csproj -c Release

# Unit + contract tests (QA-02, QA-03, QA-06)
dotnet test tests/MasjidApp.Core.Tests/MasjidApp.Core.Tests.csproj -c Release

# Validate mock JSON seed files
for f in mock/*.json; do python3 -c "import json,sys; json.load(open(sys.argv[1]))" "$f" && echo "✓ $f"; done
```

> **Contract tests** in `tests/MasjidApp.Core.Tests/ContractTests/` read the actual files from
> `mock/` and deserialize them into the app's models.  If a mock file or a model changes
> incompatibly, these tests fail before CI sees it.

## Continuous Integration

[`.github/workflows/ci.yml`](../.github/workflows/ci.yml) runs on every push/PR to `main`
(`DV-05`): restore, format check, warnings-as-errors build, unit tests, contract tests, and JSON
validation of the seed data.  The `build-test` job runs on `ubuntu-latest` and covers
`MasjidApp.Core` + `MasjidApp.Core.Tests`; the MAUI app head (iOS/Android) is excluded because
it requires the MAUI workload.
