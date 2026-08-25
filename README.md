# Masjid App

Cross-platform mobile app (.NET MAUI) for prayer times, events, announcements, donations, push
notifications, and home-screen widgets, integrated with the Masjid API (a WordPress plugin).
Targets **iOS 15+** and **Android 8.0 / API 26+** from a single shared codebase.

- Full spec: [docs/REQUIREMENTS.md](docs/REQUIREMENTS.md)
- Current architecture and implementation status: [docs/DESIGN.md](docs/DESIGN.md)
- Developer setup & workflow: [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)
- iOS release runbook: [docs/PUBLISHING.md](docs/PUBLISHING.md)

## Prerequisites (macOS)

The app builds on macOS (required for iOS). You need:

| Tool | Purpose |
|------|---------|
| **Homebrew** | Package manager used to install the rest |
| **.NET SDK** (pinned by [global.json](global.json)) + **MAUI workload** | Build the app and Core library |
| **Microsoft OpenJDK 21** | Required by the .NET MAUI Android toolchain |
| **Android SDK** (platform-tools, platform/build-tools API 36, emulator, a system image) + an AVD | Build & run on Android |
| **Docker Desktop** | Local WordPress + mock backend (`DV-01`) |
| **Xcode** + an iOS Simulator runtime | Build & run on iOS |
| **VS Code** + the **.NET MAUI** extension | Recommended editor / debugger |

## Quick start

### 1. Bootstrap a clean machine

From the repo root, run the macOS bootstrap script. It installs and configures everything above and
is safe to re-run (idempotent):

```sh
./scripts/bootstrap-macos.sh
```

You may be prompted for your macOS password (sudo) when it installs the JDK and Docker. The script
cannot install full **Xcode** - get it from the App Store first (or install it later and re-run the
script); it will print the follow-up commands (`xcodebuild -downloadPlatform iOS`).

After it finishes, open a new terminal (or `source ~/.zshrc`) so `PATH`, `JAVA_HOME`, and
`ANDROID_HOME` are loaded.

### 2. Start the local backend

```sh
cd devbox && docker compose up -d
```

This runs WordPress + the Masjid API plugin and a static mock API. See
[docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) for the full service list and URLs.

### 3. Build, test, and run

```sh
# Unit + contract tests (no MAUI workload needed)
dotnet test tests/MasjidApp.Core.Tests/MasjidApp.Core.Tests.csproj -c Release

# Start the Android emulator and run the app
./scripts/start-android.sh

# Start the iOS Simulator and run the app
./scripts/start-ios.sh
```

> **VS Code alternative:** Run **Terminal → Run Task → `masjid: prepare-android`** (or
> `masjid: prepare-ios`). These compound tasks start Docker _and_ boot the emulator/simulator
> in parallel. When debugging with F5 the **Android - Debug** / **iOS - Debug** launch
> configurations trigger the same tasks automatically - no manual script needed.

## Project layout

```
MasjidApp.slnx
src/
  MasjidApp/            # .NET MAUI app head (iOS + Android)
  MasjidApp.Core/       # platform-agnostic domain logic (no MAUI deps) - fully unit-tested
tests/
  MasjidApp.Core.Tests/ # xUnit + FluentAssertions
backend/                # WordPress masjid-api plugin source (mounted into the devbox container)
devbox/                 # Docker Compose dev backend (WordPress, mock API, tooling)
mock/                   # Seed JSON/CSV used by the mock API and contract tests
scripts/                # Developer tooling (bootstrap-macos.sh, start-android.sh, start-ios.sh)
docs/                   # REQUIREMENTS.md, DESIGN.md, DEVELOPMENT.md
```

## Definition of Done

Run these before opening a PR - CI enforces the same gates (`QA-01`, `QA-02`):

```sh
dotnet format src/MasjidApp.Core/MasjidApp.Core.csproj --verify-no-changes --severity warn
dotnet build  src/MasjidApp.Core/MasjidApp.Core.csproj -c Release
dotnet test   tests/MasjidApp.Core.Tests/MasjidApp.Core.Tests.csproj -c Release
```

See [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) for environment configuration, the WordPress endpoints,
and the full CI check list.
